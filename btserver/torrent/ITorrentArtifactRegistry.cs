using boottorrent_lib.torrent;
using MonoTorrent;

namespace btserver.torrent;

public interface ITorrentArtifactRegistry
{
    Task<Dictionary<string, TorrentArtifact>> GetRegisteredArtifacts();
    Task<TorrentArtifact> CreateAndRegisterTorrentAsync(string name, string description, string filePath, CancellationToken cancellationToken);
    Task<TorrentArtifact> GetArtifactByIdAsync(string artifactId);
    Task<string> GetTorrentFilePathAsync(string artifactId);
    Task<string> GetArtifactContentPathAsync(string artifactId);
    TorrentArtifact? GetArtifactByInfoHash(string announceRequestInfoHash);
    
    event EventHandler<TorrentArtifact> ArtifactRegistered;
    event EventHandler<TorrentArtifact> ArtifactUnRegistered;
}