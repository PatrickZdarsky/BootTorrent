using System.Net;
using MonoTorrent;
using MonoTorrent.Connections.Peer;

namespace btserver.torrent;

public interface ITorrentSeeder
{
    Task<List<string>> GetSeededTorrents();

    string getClientId();
    
    IPEndPoint GetClientEndpoint();
}