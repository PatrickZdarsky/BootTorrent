using boottorrent_lib.torrent;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Connections;

namespace btclient.torrent.monotorrent;

public class MonoTorrentClient(
    ILogger<MonoTorrentClient> logger,
    OriginPeerConnectionPolicy originPolicy,
    IOptions<BTClientSettings> clientSettings) : ITorrentClient, IOriginPeerConnectionController
{
    public ClientEngine? engine;
    private CancellationTokenSource? _announceCancellation;
    private Task? _announceTask;

    public async Task<ITorrentStatus> AddTorrentAsync(TorrentJob torrentJob, string torrentFilePath, string downloadPath)
    {
        var torrent = await Torrent.LoadAsync(torrentFilePath);
        var manager = await engine.AddAsync(torrent, downloadPath, 
            new TorrentSettingsBuilder{AllowDht = false, AllowPeerExchange = false}.ToSettings());
        manager.PeersFound += (_, args) =>
        {
            logger.LogInformation("Found {PeerCount} peers for torrent {TorrentName} with info hash {InfoHash}",
                args.NewPeers, manager.Torrent.Name, manager.Torrent.InfoHashes.V1OrV2.ToHex());
        };
        manager.PeerConnected += (_, args) =>
        {
            logger.LogInformation("Connected to peer {PeerEndpoint} for torrent {TorrentName} with info hash {InfoHash}",
                args.Peer.Uri, manager.Torrent.Name, manager.Torrent.InfoHashes.V1OrV2.ToHex());
        };
        manager.TorrentStateChanged += (_, args) =>
        {
            logger.LogInformation("Torrent {TorrentName} with info hash {InfoHash} changed state from {OldState} to {NewState}",
                manager.Torrent.Name, manager.Torrent.InfoHashes.V1OrV2.ToHex(), args.OldState, args.NewState);
        };
        
        await manager.StartAsync();
        logger.LogInformation("Started downloading torrent {TorrentName} with info hash {InfoHash}", torrent.Name, torrent.InfoHashes.V1OrV2.ToHex());
        
        return new MonoTorrentStatus(manager) { TorrentJob = torrentJob };
    }

    public async Task RemoveTorrentAsync(string infoHash)
    {
        var manager = engine.Torrents.FirstOrDefault(m => m.InfoHashes.V1OrV2.ToHex().Equals(infoHash, StringComparison.OrdinalIgnoreCase));
        if (manager != null)
        {
            logger.LogInformation("Stopping torrent {TorrentName} with info hash {InfoHash}", manager.Torrent.Name, infoHash);
            await manager.StopAsync();
            await engine.RemoveAsync(manager);
            logger.LogInformation("Removed torrent {TorrentName} with info hash {InfoHash}", manager.Torrent.Name, infoHash);
        }
        else
        {
            logger.LogWarning("Could not find torrent with info hash {InfoHash} to remove", infoHash);
        }
    }

    public async Task StartAsync()
    {
        logger.LogInformation("Starting MonoTorrent engine");

        var settings = new EngineSettingsBuilder
        {
            AllowPortForwarding = false,
            AutoSaveLoadDhtCache = false,
            AutoSaveLoadFastResume = false,
            AutoSaveLoadMagnetLinkMetadata = false,
            AllowLocalPeerDiscovery = false,
            StaleRequestTimeout = TimeSpan.FromSeconds(5),
            AllowedEncryption = [EncryptionType.RC4Full, EncryptionType.RC4Header],
            MaximumDownloadRate = clientSettings.Value.MaximumDownloadRateBytesPerSecond,
        }.ToSettings();

        engine = new ClientEngine(settings);
        engine.ConnectionManager.BanPeer += (_, args) =>
        {
            if (!originPolicy.ShouldReject(args.Peer.ConnectionUri)) return;
            args.BanPeer = true;
            logger.LogInformation("Rejected central seeder connection to {PeerEndpoint}; machine is not a proxy.",
                args.Peer.ConnectionUri);
        };
        _announceCancellation = new CancellationTokenSource();
        _announceTask = AnnounceWhileDownloadingAsync(engine, _announceCancellation.Token);
        logger.LogInformation("MonoTorrent engine started");
    }

    private async Task AnnounceWhileDownloadingAsync(ClientEngine activeEngine, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                foreach (var manager in activeEngine.Torrents.ToList())
                {
                    if (manager.State != TorrentState.Downloading) continue;
                    try
                    {
                        // MonoTorrent respects the tracker's update interval.
                        // Refresh discovery when a peer or its port changes.
                        await manager.TrackerManager.AnnounceAsync(token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Could not refresh tracker peers for {TorrentName}.", manager.Torrent.Name);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public async Task ResetConnectionsAsync()
    {
        if (engine is null) return;
        foreach (var manager in engine.Torrents.ToList())
        {
            await manager.StopAsync();
            await manager.StartAsync();
        }
    }

    public async Task RefreshTrackerAsync()
    {
        if (engine is null) return;
        foreach (var manager in engine.Torrents.ToList())
        {
            // A role promotion must discover the origin promptly without
            // dropping the peer connections and listening port already in use.
            await manager.TrackerManager.AnnounceAsync(TorrentEvent.Started, CancellationToken.None);
        }
    }

    public async Task StopAsync()
    {
        if (engine != null)
        {
            _announceCancellation?.Cancel();
            if (_announceTask is not null) await _announceTask;
            _announceCancellation?.Dispose();
            _announceCancellation = null;
            _announceTask = null;
            logger.LogInformation("Stopping MonoTorrent engine");
            await engine.StopAllAsync();
            engine.Dispose();
            engine = null;
            logger.LogInformation("MonoTorrent engine stopped");
        }
    }

    public List<ITorrentStatus> GetActiveTorrents()
    {
        return engine?.Torrents.Select(ITorrentStatus (m) => new MonoTorrentStatus(m)).ToList() ?? new List<ITorrentStatus>();
    }
}
