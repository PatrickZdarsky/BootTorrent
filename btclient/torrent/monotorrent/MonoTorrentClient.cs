using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Connections;

namespace btclient.torrent.monotorrent;

public class MonoTorrentClient : ITorrentClient
{
    private ClientEngine _engine;
    private readonly ILogger<MonoTorrentClient> _logger;
    private readonly BTClientSettings _clientSettings;

    public MonoTorrentClient(ILogger<MonoTorrentClient> logger, IOptions<BTClientSettings> clientSettings)
    {
        _logger = logger;
        _clientSettings = clientSettings.Value;
    }

    public async Task AddTorrentAsync(string torrentFilePath)
    {
        var torrent = await Torrent.LoadAsync(torrentFilePath);
        var manager = await _engine.AddAsync(torrent, _clientSettings.DownloadPath);
        manager.PeersFound += (sender, args) =>
        {
            _logger.LogInformation("Found {PeerCount} peers for torrent {TorrentName} with info hash {InfoHash}",
                args.NewPeers, manager.Torrent.Name, manager.Torrent.InfoHashes.V1OrV2);
        };
        manager.PeerConnected += (sender, args) =>
        {
            _logger.LogInformation("Connected to peer {PeerEndpoint} for torrent {TorrentName} with info hash {InfoHash}",
                args.Peer.Uri, manager.Torrent.Name, manager.Torrent.InfoHashes.V1OrV2);
        };
        manager.TorrentStateChanged += (sender, args) =>
        {
            _logger.LogInformation("Torrent {TorrentName} with info hash {InfoHash} changed state from {OldState} to {NewState}",
                manager.Torrent.Name, manager.Torrent.InfoHashes.V1OrV2, args.OldState, args.NewState);
        };
        
        await manager.StartAsync();
        _logger.LogInformation("Started downloading torrent {TorrentName} with info hash {InfoHash}", torrent.Name, torrent.InfoHashes.V1OrV2);
    }

    public async Task RemoveTorrentAsync(string infoHash)
    {
        var manager = _engine.Torrents.FirstOrDefault(m => m.InfoHashes.V1OrV2.ToHex().Equals(infoHash, StringComparison.OrdinalIgnoreCase));
        if (manager != null)
        {
            _logger.LogInformation("Stopping torrent {TorrentName} with info hash {InfoHash}", manager.Torrent.Name, infoHash);
            await manager.StopAsync();
            await _engine.RemoveAsync(manager);
            _logger.LogInformation("Removed torrent {TorrentName} with info hash {InfoHash}", manager.Torrent.Name, infoHash);
        }
        else
        {
            _logger.LogWarning("Could not find torrent with info hash {InfoHash} to remove", infoHash);
        }
    }

    public async Task StartAsync()
    {
        _logger.LogInformation("Starting MonoTorrent engine");

        var settings = new EngineSettingsBuilder
        {
            AllowPortForwarding = false,
            AutoSaveLoadDhtCache = false,
            AutoSaveLoadFastResume = false,
            AutoSaveLoadMagnetLinkMetadata = false,
            AllowedEncryption = [EncryptionType.RC4Full, EncryptionType.RC4Header],
        }.ToSettings();

        _engine = new ClientEngine(settings);
        _logger.LogInformation("MonoTorrent engine started");
    }

    public async Task StopAsync()
    {
        if (_engine != null)
        {
            _logger.LogInformation("Stopping MonoTorrent engine");
            await _engine.StopAllAsync();
            _engine.Dispose();
            _engine = null;
            _logger.LogInformation("MonoTorrent engine stopped");
        }
    }
}