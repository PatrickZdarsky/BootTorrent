using boottorrent_lib.torrent;

namespace btserver.torrent;

public interface ITorrentSeeder
{
    Task SeedArtifacts(List<TorrentArtifact> artifacts);
}