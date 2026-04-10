using MonoTorrent.BEncoding;

namespace btserver.torrent.monotorrent;

using System.Collections.Concurrent;
using System.Net;
using MonoTorrent;
using MonoTorrent.Client;
using Microsoft.Extensions.Logging;

public class MonoTorrentSeederService : ITorrentSeederService, ITorrentSeeder, IAsyncDisposable
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
    
        // Create a list of all the StopAsync tasks
        var stopTasks = _managers.Values.Select(manager =>
        {
            try
            {
                // Return the task so it can be awaited by Task.WhenAll
                return manager.StopAsync(TimeSpan.FromSeconds(1));
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to stop torrent manager for '{InfoHash}'", manager.InfoHashes);
                // Return a completed task if an exception occurs synchronously
                return Task.CompletedTask;
            }
        }).ToList();
    
        try
        {
            // Await all the stop tasks to complete in parallel
            await Task.WhenAll(stopTasks);
        }
        catch (Exception e)
        {
            // This will catch any exceptions from the awaited tasks
            _logger.LogWarning(e, "An error occurred while stopping torrent managers in parallel.");
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
            
            var torrentPath = await _artifactRegistry.GetTorrentFilePathAsync(artifactId);
            var dataPath = await _artifactRegistry.GetArtifactContentPathAsync(artifactId);
            //Strip file from path
            dataPath = Path.GetDirectoryName(dataPath) ?? throw new InvalidOperationException($"Failed to get directory name from artifact content path '{dataPath}'");
            
            var torrent = await Torrent.LoadAsync(torrentPath);

            _logger.LogInformation("Ensuring artifact '{ArtifactId}'({InfoHash}) is seeding", artifactId, torrent.InfoHashes.V1OrV2.ToHex());

            
            var manager = await _engine.AddAsync(torrent, dataPath, new TorrentSettingsBuilder
            {
                AllowDht = false,
                AllowInitialSeeding = true,
                AllowPeerExchange = true
            }.ToSettings());

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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        await StopAsync(CancellationToken.None);
    }
}