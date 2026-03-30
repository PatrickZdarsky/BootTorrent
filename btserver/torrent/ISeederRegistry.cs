namespace btserver.torrent;

public interface ISeederRegistry
{
    void RegisterSeeder(ITorrentSeeder seeder);
    void UnregisterSeeder(ITorrentSeeder seeder);
    Task<List<ITorrentSeeder>> GetSeedersForTorrent(string infoHash);
}