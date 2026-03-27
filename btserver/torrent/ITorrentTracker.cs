namespace btserver.torrent;

public interface ITorrentTracker
{
    Task RegisterSeeder();
}