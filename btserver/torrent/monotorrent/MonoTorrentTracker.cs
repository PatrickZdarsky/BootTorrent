using System.Collections.Concurrent;
using boottorrent_lib.torrent;
using btserver.settings;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.BEncoding;
using MonoTorrent.Connections.TrackerServer;
using MonoTorrent.TrackerServer;

namespace btserver.torrent.monotorrent;

public sealed class MonoTorrentTracker : IHostedService, IDisposable
{
    private readonly IOptions<TrackerSettings> _settings;
    private readonly ITorrentAccessPolicy _accessPolicy;
    private readonly ILogger<MonoTorrentTracker> _logger;

    private readonly SemaphoreSlim _announceLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TorrentArtifactTrackable> _registeredTorrents = new();

    private TrackerServer? _trackerServer;
    private ITrackerListener? _listener;
    private bool _disposed;

    public MonoTorrentTracker(
        IOptions<TrackerSettings> settings,
        ITorrentAccessPolicy accessPolicy,
        ILogger<MonoTorrentTracker> logger)
    {
        _settings = settings;
        _accessPolicy = accessPolicy;
        _logger = logger;
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

            var requestedInfoHash = request.InfoHash.ToHex();
            var availableTorrents = await _accessPolicy.GetAvailableTorrentsAsync(clientId);

            var allowedTorrent = availableTorrents
                .FirstOrDefault(t => string.Equals(
                    NormalizeInfoHash(t.InfoHash),
                    NormalizeInfoHash(requestedInfoHash),
                    StringComparison.OrdinalIgnoreCase));

            if (allowedTorrent is null)
            {
                _logger.LogWarning(
                    "Rejecting announce for client {ClientId}. InfoHash {InfoHash} is not allowed.",
                    clientId,
                    requestedInfoHash);

                SetFailure(request, "This client is not allowed to access the requested torrent.");
                return;
            }

            // Ensure the torrent is registered before TrackerServer handles the announce.
            await EnsureTrackableRegisteredAsync(allowedTorrent);

            _logger.LogDebug(
                "Accepted announce for client {ClientId}, torrent {TorrentName} ({InfoHash})",
                clientId,
                allowedTorrent.Name,
                allowedTorrent.InfoHash);
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

    private static string BuildAnnounceUrl(TrackerSettings settings)
    {
        var host = string.IsNullOrWhiteSpace(settings.BindAddress)
            ? "0.0.0.0"
            : settings.BindAddress;

        return $"http://{host}:{settings.Port}/announce/";
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