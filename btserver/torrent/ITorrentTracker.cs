using boottorrent_lib.torrent;

namespace btserver.torrent;

public interface ITorrentTracker
{
    void RegisterSeeder(ITorrentSeeder torrentSeeder);
    void UnregisterSeeder(ITorrentSeeder seeder);
    
    Task RegisterArtifact(TorrentArtifact artifact);
}