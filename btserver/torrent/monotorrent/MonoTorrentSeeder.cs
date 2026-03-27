namespace btserver.torrent.monotorrent;

using System.Collections.Concurrent;
using System.Net;
using MonoTorrent;
using MonoTorrent.Client;
using Microsoft.Extensions.Logging;

public class MonoTorrentSeeder(
    ITorrentArtifactRegistry artifactRegistry,
    ILogger<MonoTorrentSeeder> logger) : IHostedService, IDisposable
{
    private readonly ConcurrentDictionary<string, TorrentManager> _managers = new();
    private readonly SemaphoreSlim _sync = new(1, 1);

    private ClientEngine? _engine;
    private bool _disposed;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        logger.LogInformation("Starting MonoTorrent engine");

        var settings = new EngineSettingsBuilder
        {
            ListenEndPoints = new Dictionary<string, IPEndPoint>
            {
                ["ipv4"] = new IPEndPoint(IPAddress.Any, 55123)
            },

            AllowPortForwarding = false,
            AutoSaveLoadDhtCache = false,
            AutoSaveLoadFastResume = false,
            AutoSaveLoadMagnetLinkMetadata = false
        }.ToSettings();

        _engine = new ClientEngine(settings);

        logger.LogInformation("MonoTorrent engine started");

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_engine is null)
            return;

        logger.LogInformation("Stopping MonoTorrent engine");

        foreach (var manager in _managers.Values)
        {
            try
            {
                await manager.StopAsync();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to stop torrent manager for '{InfoHash}'", manager.InfoHashes);
            }
        }

        _managers.Clear();

        _engine.Dispose();
        _engine = null;

        logger.LogInformation("MonoTorrent engine stopped");
    }

    /// <summary>
    /// Ensures that the given torrent is loaded into MonoTorrent and actively seeding.
    /// </summary>
    public async Task EnsureSeedingAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_engine is null)
            throw new InvalidOperationException("Seeder has not been started yet.");

        if (_managers.ContainsKey(artifactId))
        {
            logger.LogDebug("Artifact '{ArtifactId}' is already seeding", artifactId);
            return;
        }

        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (_managers.ContainsKey(artifactId))
            {
                logger.LogDebug("Artifact '{ArtifactId}' is already seeding (checked after lock)", artifactId);
                return;
            }

            logger.LogInformation("Ensuring artifact '{ArtifactId}' is seeding", artifactId);

            var torrentPath = await artifactRegistry.GetTorrentFilePathAsync(artifactId, cancellationToken);
            var dataPath = await artifactRegistry.GetArtifactContentPathAsync(artifactId, cancellationToken);

            var torrent = await Torrent.LoadAsync(torrentPath);
            var manager = await _engine.AddAsync(torrent, dataPath);

            await manager.StartAsync();
            logger.LogInformation("Started seeding artifact '{ArtifactId}' with info hash '{InfoHash}'",
                artifactId, manager.InfoHashes);

            _managers[artifactId] = manager;
        }
        finally
        {
            _sync.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _sync.Dispose();

        foreach (var manager in _managers.Values)
        {
            try
            {
                manager.StopAsync().RunSynchronously();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to stop torrent manager on dispose for '{InfoHash}'", manager.InfoHashes);
            }
        }

        _managers.Clear();

        _engine?.Dispose();
        _engine = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}