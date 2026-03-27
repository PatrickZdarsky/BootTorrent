using boottorrent_lib.torrent;

namespace btserver.torrent;

public interface ITorrentCreator
{
    List<TorrentArtifact> LoadedArtifacts { get; }
    Task<TorrentArtifact> GenerateTorrentArtifactAsync(string name, string description, string filePath);
    Task LoadExistingArtifactsAsync();

    string ConstructArtifactPathFromArtifact(TorrentArtifact artifact);
    string ConstructTorrentPathFromArtifact(TorrentArtifact torrent);
}