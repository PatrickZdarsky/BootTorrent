using System.Net;

namespace btserver.torrent;

public interface ITorrentSeeder
{
    Task<List<string>> GetSeededTorrents();
    
    Task<IPEndPoint> GetClientEndpoint();
}