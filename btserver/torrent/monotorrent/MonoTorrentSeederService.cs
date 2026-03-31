using MonoTorrent.BEncoding;

namespace btserver.torrent.monotorrent;

using System.Collections.Concurrent;
using System.Net;
using MonoTorrent;
using MonoTorrent.Client;
using Microsoft.Extensions.Logging;

public class MonoTorrentSeederService : ITorrentSeederService, ITorrentSeeder, IDisposable
{
    private readonly ConcurrentDictionary<string, TorrentManager> _managers = new();
    private readonly SemaphoreSlim _sync = new(1, 1);

    private ClientEngine? _engine;
    private bool _disposed;
    private readonly ITorrentArtifactRegistry _artifactRegistry;
    private readonly ILogger<MonoTorrentSeederService> _logger;

    public MonoTorrentSeederService(ITorrentArtifactRegistry artifactRegistry,
        ILogger<MonoTorrentSeederService> logger)
    {
        _artifactRegistry = artifactRegistry;
        _logger = logger;
        
        _artifactRegistry.ArtifactRegistered += async (sender, artifact) =>
        {
            try
            {
                await EnsureSeedingAsync(artifact.ID);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to ensure seeding for newly registered artifact with ID '{ArtifactId}'", artifact.ID);
            }
        };
        _artifactRegistry.ArtifactUnRegistered += async (sender, artifact) =>
        {
            try
            {
                await StopSeedingAsync(artifact.ID);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to stop seeding for unregistered artifact with ID '{ArtifactId}'", artifact.ID);
            }
        };
    }

    private async Task StopSeedingAsync(string artifactId)
    {
        if (!_managers.ContainsKey(artifactId))
        {
            _logger.LogDebug("Artifact '{ArtifactId}' is not currently seeding, no need to stop", artifactId);
            return;
        }

        await _sync.WaitAsync();
        try
        {
            if (!_managers.ContainsKey(artifactId))
            {
                _logger.LogDebug("Artifact '{ArtifactId}' is not currently seeding, no need to stop (checked after lock)", artifactId);
                return;
            }

            _logger.LogInformation("Stopping seeding for artifact '{ArtifactId}'", artifactId);

            var manager = _managers[artifactId];
            await manager.StopAsync();
            _managers.TryRemove(artifactId, out _);
            _logger.LogInformation("Stopped seeding for artifact '{ArtifactId}' with info hash '{InfoHash}'",
                artifactId, manager.InfoHashes);
        } catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to stop seeding for artifact with ID '{ArtifactId}'", artifactId);
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
    
        _logger.LogInformation("Starting MonoTorrent engine");
    
        var settings = new EngineSettingsBuilder
        {
            ListenEndPoints = new Dictionary<string, IPEndPoint>
            {
                ["ipv4"] = GetClientEndpoint()
            },
            
            AllowPortForwarding = false,
            AutoSaveLoadDhtCache = false,
            AutoSaveLoadFastResume = false,
            AutoSaveLoadMagnetLinkMetadata = false
        }.ToSettings();
    
        _engine = new ClientEngine(settings);
        var customPeerId = new BEncodedString("boottorrent-server-seeder"); // Must be 20 bytes
    
        // Use reflection to set the read-only property's backing field
        var field = typeof(ClientEngine).GetField("<PeerId>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field != null)
        {
            field.SetValue(_engine, customPeerId);
        }
    
        _logger.LogInformation("MonoTorrent seeding engine started ({peerId})", _engine.PeerId);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_engine is null)
            return;

        _logger.LogInformation("Stopping MonoTorrent seeding engine");

        foreach (var manager in _managers.Values)
        {
            try
            {
                await manager.StopAsync();
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to stop torrent manager for '{InfoHash}'", manager.InfoHashes);
            }
        }

        _managers.Clear();

        _engine.Dispose();
        _engine = null;

        _logger.LogInformation("MonoTorrent seeding engine stopped");
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
            _logger.LogDebug("Artifact '{ArtifactId}' is already seeding", artifactId);
            return;
        }

        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (_managers.ContainsKey(artifactId))
            {
                _logger.LogDebug("Artifact '{ArtifactId}' is already seeding (checked after lock)", artifactId);
                return;
            }

            _logger.LogInformation("Ensuring artifact '{ArtifactId}' is seeding", artifactId);

            var torrentPath = await _artifactRegistry.GetTorrentFilePathAsync(artifactId);
            var dataPath = await _artifactRegistry.GetArtifactContentPathAsync(artifactId);

            var torrent = await Torrent.LoadAsync(torrentPath);
            var manager = await _engine.AddAsync(torrent, dataPath);

            await manager.StartAsync();
            _logger.LogInformation("Started seeding artifact '{ArtifactId}' with info hash '{InfoHash}'",
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
                _logger.LogWarning(e, "Failed to stop torrent manager on dispose for '{InfoHash}'", manager.InfoHashes);
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

    public Task<List<string>> GetSeededTorrents()
    {
        var hashes = new List<string>();
        foreach (var manager in _managers.Values)
        {
            hashes.Add(manager.InfoHashes.V1!.ToHex());
            hashes.Add(manager.InfoHashes.V2!.ToHex());
        }
        return Task.FromResult(hashes);
    }

    public string getClientId()
    {
        return "btserver-seeder";
    }

    public IPEndPoint GetClientEndpoint()
    {
        //Todo: Make me configureable
        return new IPEndPoint(IPAddress.Any, 55123);
    }
}