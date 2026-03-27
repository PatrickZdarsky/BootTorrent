using System.Collections.Concurrent;
using boottorrent_lib.torrent;

namespace btserver.torrent.impl;

public class TorrentArtifactRegistry(
    ILogger<TorrentArtifactRegistry> logger,
    ITorrentCreator torrentCreator
    ) : ITorrentArtifactRegistry
{
    private readonly ConcurrentDictionary<string, TorrentArtifact> _artifacts = new();
    private readonly SemaphoreSlim _sync = new(1, 1);
    
    public Task<TorrentArtifact> CreateAndRegisterTorrentAsync(string name, string description, string filePath, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<TorrentArtifact> GetArtifactByIdAsync(string artifactId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<string> GetTorrentFilePathAsync(string artifactId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<string> GetArtifactContentPathAsync(string artifactId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}