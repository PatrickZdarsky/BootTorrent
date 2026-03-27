using boottorrent_lib.torrent;
using MonoTorrent;

namespace btserver.torrent;

public interface ITorrentArtifactRegistry
{
    Task<TorrentArtifact> CreateAndRegisterTorrentAsync(string name, string description, string filePath, CancellationToken cancellationToken);
    Task<TorrentArtifact> GetArtifactByIdAsync(string artifactId, CancellationToken cancellationToken);
    Task<string> GetTorrentFilePathAsync(string artifactId, CancellationToken cancellationToken);
    Task<string> GetArtifactContentPathAsync(string artifactId, CancellationToken cancellationToken);
}