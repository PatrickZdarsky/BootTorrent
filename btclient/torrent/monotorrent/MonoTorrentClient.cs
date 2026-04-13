using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Connections;

namespace btclient.torrent.monotorrent;

public class MonoTorrentClient(ILogger<MonoTorrentClient> logger) : ITorrentClient
{
    public ClientEngine? engine;

    public async Task<ITorrentStatus> AddTorrentAsync(string torrentFilePath, string downloadPath)
    {
        var torrent = await Torrent.LoadAsync(torrentFilePath);
        var manager = await engine.AddAsync(torrent, downloadPath, 
            new TorrentSettingsBuilder{AllowDht = false}.ToSettings());
        manager.PeersFound += (_, args) =>
        {
            logger.LogInformation("Found {PeerCount} peers for torrent {TorrentName} with info hash {InfoHash}",
                args.NewPeers, manager.Torrent.Name, manager.Torrent.InfoHashes.V1OrV2);
        };
        manager.PeerConnected += (_, args) =>
        {
            logger.LogInformation("Connected to peer {PeerEndpoint} for torrent {TorrentName} with info hash {InfoHash}",
                args.Peer.Uri, manager.Torrent.Name, manager.Torrent.InfoHashes.V1OrV2);
        };
        manager.TorrentStateChanged += (_, args) =>
        {
            logger.LogInformation("Torrent {TorrentName} with info hash {InfoHash} changed state from {OldState} to {NewState}",
                manager.Torrent.Name, manager.Torrent.InfoHashes.V1OrV2, args.OldState, args.NewState);
        };
        
        await manager.StartAsync();
        logger.LogInformation("Started downloading torrent {TorrentName} with info hash {InfoHash}", torrent.Name, torrent.InfoHashes.V1OrV2);
        
        return new MonoTorrentStatus(manager);
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
            StaleRequestTimeout = TimeSpan.FromSeconds(5),
            AllowedEncryption = [EncryptionType.RC4Full, EncryptionType.RC4Header],
        }.ToSettings();

        engine = new ClientEngine(settings);
        logger.LogInformation("MonoTorrent engine started");
    }

    public async Task StopAsync()
    {
        if (engine != null)
        {
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