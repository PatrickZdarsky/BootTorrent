using System.Collections.Concurrent;
using boottorrent_lib.torrent;

namespace btserver.torrent.impl;

public class TorrentArtifactRegistry(
    ILogger<TorrentArtifactRegistry> logger,
    ITorrentCreator torrentCreator
    ) : ITorrentArtifactRegistry
{
    public event EventHandler<TorrentArtifact>? ArtifactRegistered;
    public event EventHandler<TorrentArtifact>? ArtifactUnRegistered;
    
    private readonly ConcurrentDictionary<string, TorrentArtifact> _artifacts = new();
    private readonly SemaphoreSlim _sync = new(1, 1);

    //Todo: Add ways to unregister artifacts

    public Task<Dictionary<string, TorrentArtifact>> GetRegisteredArtifacts()
    {
        return Task.FromResult(_artifacts.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
    }

    public async Task<TorrentArtifact> CreateAndRegisterTorrentAsync(string name, string description, string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var artifact = await torrentCreator.GenerateTorrentArtifactAsync(name, description, filePath);
            
            _artifacts[artifact.ID] = artifact;
            ArtifactRegistered?.Invoke(this, artifact);

            logger.LogInformation("Registered torrent artifact with ID '{ArtifactId}' for '{Name}'", artifact.ID, name);
            return artifact;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to create torrent artifact for '{Name}'", name);
            throw;
        }
    }

    public Task<TorrentArtifact> GetArtifactByIdAsync(string artifactId)
    {
        return Task.FromResult(_artifacts[artifactId]);
    }
    
    public TorrentArtifact? GetArtifactByInfoHash(string announceRequestInfoHash)
    {
        return _artifacts.Values.FirstOrDefault(artifact => artifact.InfoHashV1 == announceRequestInfoHash || artifact.InfoHashV2 == announceRequestInfoHash);
    }

    public Task<string> GetTorrentFilePathAsync(string artifactId)
    {
        var artifact = _artifacts[artifactId];
        return artifact is null ? throw new KeyNotFoundException($"Artifact with ID '{artifactId}' not found") : Task.FromResult(torrentCreator.ConstructTorrentPathFromArtifact(artifact));
    }

    public Task<string> GetArtifactContentPathAsync(string artifactId)
    {
        var artifact = _artifacts[artifactId];
        return artifact is null ? throw new KeyNotFoundException($"Artifact with ID '{artifactId}' not found") : Task.FromResult(torrentCreator.ConstructArtifactPathFromArtifact(artifact));
    }

    public async Task<bool> UnregisterArtifactAsync(string artifactId, bool deleteFiles, CancellationToken cancellationToken)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (!_artifacts.TryRemove(artifactId, out var artifact))
            {
                return false;
            }

            torrentCreator.LoadedArtifacts.RemoveAll(existingArtifact => string.Equals(existingArtifact.ID, artifactId, StringComparison.Ordinal));

            if (deleteFiles)
            {
                var artifactDirectoryPath = torrentCreator.ConstructArtifactDirectoryPath(artifact);
                if (Directory.Exists(artifactDirectoryPath))
                {
                    try
                    {
                        Directory.Delete(artifactDirectoryPath, recursive: true);
                    }
                    catch (IOException ex)
                    {
                        logger.LogWarning(
                            ex,
                            "Artifact {ArtifactId} was unregistered, but deleting artifact directory {ArtifactDirectoryPath} failed due to an I/O error.",
                            artifact.ID,
                            artifactDirectoryPath);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        logger.LogWarning(
                            ex,
                            "Artifact {ArtifactId} was unregistered, but deleting artifact directory {ArtifactDirectoryPath} failed due to insufficient permissions.",
                            artifact.ID,
                            artifactDirectoryPath);
                    }
                }
            }

            ArtifactUnRegistered?.Invoke(this, artifact);
            logger.LogInformation("Unregistered torrent artifact with ID '{ArtifactId}' for '{Name}'", artifact.ID, artifact.Name);
            return true;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await torrentCreator.LoadExistingArtifactsAsync();
        foreach (var artifact in torrentCreator.LoadedArtifacts)
        {
            if (_artifacts.TryAdd(artifact.ID, artifact))
            {
                ArtifactRegistered?.Invoke(this, artifact);
                logger.LogInformation("Registered pre-existing torrent artifact with ID '{ArtifactId}' for '{Name}'", artifact.ID, artifact.Name);
            }
        }
        
        await CreateAndRegisterTorrentAsync("TestArtifact", "This is a test artifact created at startup.", "/home/patrick/boottorrent/2025-04-26 00-29-58.mkv", cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
