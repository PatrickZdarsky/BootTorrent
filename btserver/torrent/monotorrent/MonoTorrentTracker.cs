using System.Collections.Concurrent;
using boottorrent_lib.torrent;
using btserver.settings;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.BEncoding;
using MonoTorrent.Connections.TrackerServer;
using MonoTorrent.TrackerServer;

namespace btserver.torrent.monotorrent;

public sealed class MonoTorrentTracker : IDisposable, ITorrentSeederRegistry, ITorrentTracker
{
    private readonly IOptions<TorrentSettings> _settings;
    private readonly ITorrentAccessPolicy _accessPolicy;
    private readonly ITorrentArtifactRegistry _artifactRegistry;
    private readonly ISeederRegistry _seederRegistry;
    private readonly ILogger<MonoTorrentTracker> _logger;

    private readonly SemaphoreSlim _announceLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TorrentArtifactTrackable> _registeredTorrents = new();

    private TrackerServer? _trackerServer;
    private ITrackerListener? _listener;
    private bool _disposed;

    public MonoTorrentTracker(
        IOptions<TorrentSettings> settings,
        ITorrentAccessPolicy accessPolicy,
        ISeederRegistry seederRegistry,
        ILogger<MonoTorrentTracker> logger, 
        ITorrentArtifactRegistry artifactRegistry)
    {
        _settings = settings;
        _accessPolicy = accessPolicy;
        _seederRegistry = seederRegistry;
        _logger = logger;
        _artifactRegistry = artifactRegistry;
        
        _artifactRegistry.ArtifactRegistered += async (sender, artifact) =>
        {
            try
            {
                await RegisterArtifact(artifact);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to register newly added artifact with ID '{ArtifactId}' to tracker", artifact.ID);
            }
        };
        _artifactRegistry.ArtifactUnRegistered += async (sender, artifact) =>
        {
            try
            {
                await UnregisterArtifact(artifact);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to unregister removed artifact with ID '{ArtifactId}' from tracker", artifact.ID);
            }
        };
    }

    private async Task UnregisterArtifact(TorrentArtifact artifact)
    {
        if (_trackerServer is null)
            throw new InvalidOperationException("Tracker server is not initialized.");

        var normalized = NormalizeInfoHash(artifact.InfoHash);
        if (!_registeredTorrents.ContainsKey(normalized))
            return;

        await _announceLock.WaitAsync();
        try
        {
            if (!_registeredTorrents.ContainsKey(normalized))
                return;

            _trackerServer.Remove(_registeredTorrents[normalized]);
            _registeredTorrents.TryRemove(normalized, out _);
            _logger.LogInformation(
                "Unregistered torrent {TorrentName} ({InfoHash}) from MonoTorrent tracker",
                artifact.Name,
                artifact.InfoHash);
        }
        finally
        {
            _announceLock.Release();
        }
    }

    public void RegisterSeeder(ITorrentSeeder seeder)
    {
        _seederRegistry.RegisterSeeder(seeder);
    }

    public void UnregisterSeeder(ITorrentSeeder seeder)
    {
        _seederRegistry.UnregisterSeeder(seeder);
    }

    public async Task RegisterArtifact(TorrentArtifact artifact)
    {
        await EnsureTrackableRegisteredAsync(artifact);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var trackerId = string.IsNullOrWhiteSpace(_settings.Value.TrackerId)
            ? null
            : new BEncodedString(_settings.Value.TrackerId);

        _trackerServer = trackerId is null
            ? new TrackerServer()
            : new TrackerServer(trackerId);

        // Recommended: keep this false, because we want to gate access ourselves.
        _trackerServer.AllowUnregisteredTorrents = false;
        _trackerServer.AllowScrape = false;

        // if (_settings.Value.AnnounceInterval is not null)
        //     _trackerServer.AnnounceInterval = _settings.Value.AnnounceInterval.Value;
        //
        // if (_settings.Value.MinAnnounceInterval is not null)
        //     _trackerServer.MinAnnounceInterval = _settings.Value.MinAnnounceInterval.Value;

        var announceUrl = BuildAnnounceUrl(_settings.Value);
        _listener = TrackerListenerFactory.CreateHttp(announceUrl);

        // Important: subscribe BEFORE RegisterListener so our handler gets the chance
        // to register/deny torrents before TrackerServer processes the announce.
        _listener.AnnounceReceived += OnAnnounceReceived;
        _listener.ScrapeReceived += (s, e) =>
        {
            // Deny all scrape requests since we don't want to expose torrent statistics.
            e.Response[TrackerRequest.FailureKey] = new BEncodedString("Scrape is not supported.");
        };

        _trackerServer.RegisterListener(_listener);
        _listener.Start();

        _logger.LogInformation("MonoTorrent tracker started on {AnnounceUrl}", announceUrl);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_listener is not null)
        {
            _listener.AnnounceReceived -= OnAnnounceReceived;
            _listener.Stop();
        }

        if (_trackerServer is not null && _listener is not null && _trackerServer.IsRegistered(_listener))
            _trackerServer.UnregisterListener(_listener);

        _listener = null;
        _trackerServer?.Dispose();
        _trackerServer = null;

        _registeredTorrents.Clear();

        _logger.LogInformation("MonoTorrent tracker stopped");
        return Task.CompletedTask;
    }

    private async void OnAnnounceReceived(object? sender, AnnounceRequest request)
    {
        // MonoTorrent event is sync, but our handler is async.
        // async void is acceptable for event handlers, so we catch everything inside.
        try
        {
            if (_trackerServer is null)
            {
                SetFailure(request, "Tracker is not initialized.");
                return;
            }

            if (!request.IsValid)
            {
                // MonoTorrent already populates the response for invalid requests.
                return;
            }

            var clientId = ExtractClientId(request.PeerId);
            if (string.IsNullOrWhiteSpace(clientId))
            {
                SetFailure(request, "Unable to derive client id from peer_id.");
                return;
            }
            
            _logger.LogInformation(
                "Received announce from peer {clientId} ({PeerId}) for torrent with InfoHash {InfoHash}",
                clientId,
                request.PeerId,
                request.InfoHash?.ToHex());

            var requestedInfoHash = request.InfoHash.ToHex();
            if (!await _accessPolicy.CanAccessInfoHash(clientId, requestedInfoHash))
            {
                _logger.LogWarning(
                    "Rejecting announce for client {ClientId}. Access to torrent with InfoHash {InfoHash} is denied by policy.",
                    clientId,
                    requestedInfoHash);

                SetFailure(request, "This client is not allowed to access the requested torrent.");
                return;
            }
            
            var trackableArtifact = _registeredTorrents.Values.FirstOrDefault(t => string.Equals(
                NormalizeInfoHash(t.InfoHash.ToHex()),
                NormalizeInfoHash(requestedInfoHash),
                StringComparison.OrdinalIgnoreCase));
            
            if (trackableArtifact is null)            
            {
                _logger.LogWarning(
                    "Rejecting announce for client {ClientId}. Torrent with InfoHash {InfoHash} is not registered.",
                    clientId,
                    requestedInfoHash);

                SetFailure(request, "Requested torrent is not available on this tracker.");
                return;
            }

            var artifact = trackableArtifact.Source;

            var seeders = await _seederRegistry.GetSeedersForTorrent(artifact.InfoHash);
            if (seeders.Count == 0)
            {
                _logger.LogWarning("No seeders found for torrent {TorrentName} ({InfoHash})",
                    artifact.Name,
                    artifact.InfoHash);
            }
            else
            {
                _logger.LogInformation(
                    "Found {SeederCount} seeders for torrent {TorrentName} ({InfoHash})",
                    seeders.Count,
                    artifact.Name,
                    artifact.InfoHash);

                var peers = new BEncodedList();
                seeders.ForEach(s =>
                {
                    var endpoint = s.GetClientEndpoint();
                    var peerDict = new BEncodedDictionary
                    {
                        ["ip"] = new BEncodedString(endpoint.Address.ToString()),
                        ["port"] = new BEncodedNumber(endpoint.Port)
                    };
                    peers.Add(peerDict);
                });

                request.Response["peers"] = peers;
            }

            _logger.LogDebug(
                "Accepted announce for client {ClientId}, torrent {TorrentName} ({InfoHash})",
                clientId,
                artifact.Name,
                artifact.InfoHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while processing tracker announce");
            SetFailure(request, "Internal tracker error.");
        }
    }

    private async Task EnsureTrackableRegisteredAsync(TorrentArtifact torrent)
    {
        if (_trackerServer is null)
            throw new InvalidOperationException("Tracker server is not initialized.");

        var normalized = NormalizeInfoHash(torrent.InfoHash);
        if (_registeredTorrents.ContainsKey(normalized))
            return;

        await _announceLock.WaitAsync();
        try
        {
            if (_registeredTorrents.ContainsKey(normalized))
                return;

            var trackable = new TorrentArtifactTrackable(torrent);

            if (_trackerServer.Add(trackable))
            {
                _registeredTorrents[normalized] = trackable;

                _logger.LogInformation(
                    "Registered torrent {TorrentName} ({InfoHash}) with MonoTorrent tracker",
                    torrent.Name,
                    torrent.InfoHash);
            }
            else
            {
                // Add returns false if it is already known or not accepted.
                // Re-check presence to avoid false negatives in races.
                _registeredTorrents.TryAdd(normalized, trackable);
            }
        }
        finally
        {
            _announceLock.Release();
        }
    }

    private static string BuildAnnounceUrl(TorrentSettings settings)
    {
        var host = string.IsNullOrWhiteSpace(settings.TrackerBindAddress)
            ? "*"
            : settings.TrackerBindAddress;

        return $"http://{host}:{settings.TrackerPort}/announce/";
    }

    private static string ExtractClientId(BEncodedString peerId)
    {
        // This assumes your own clients encode their client id into the peer_id.
        // Commonly, peer_id is a 20-byte identifier. How you derive a client id from it
        // depends on your own peer_id format.
        //
        // Example format expectation:
        //   "client-12345________"
        //
        // Adjust this to your actual peer_id scheme.
        var raw = peerId?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        return raw.Trim();
    }

    private static string NormalizeInfoHash(string infoHash)
        => infoHash.Trim().ToLowerInvariant();

    private static void SetFailure(AnnounceRequest request, string message)
    {
        request.Response[TrackerRequest.FailureKey] = new BEncodedString(message);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MonoTorrentTracker));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _announceLock.Dispose();
        _trackerServer?.Dispose();
    }

    private sealed class TorrentArtifactTrackable : ITrackable
    {
        public InfoHash InfoHash { get; }
        public string Name { get; }

        public TorrentArtifact Source { get; }

        public TorrentArtifactTrackable(TorrentArtifact source)
        {
            Source = source;
            Name = source.Name;
            InfoHash = InfoHash.FromHex(source.InfoHash);
        }
    }
}