using boottorrent_lib.torrent;

namespace btserver.torrent;

public interface ITorrentCreator
{
    public Task<TorrentArtifact> GenerateTorrentArtifactAsync(string name, string description, string filePath);
    Task LoadExistingArtifactsAsync();
}