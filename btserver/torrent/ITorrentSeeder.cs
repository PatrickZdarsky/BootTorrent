using System.Net;
using MonoTorrent;
using MonoTorrent.Connections.Peer;

namespace btserver.torrent;

public interface ITorrentSeeder
{
    Task<List<string>> GetSeededTorrents();

    string getClientId();
    
    IPEndPoint GetClientEndpoint();

    PeerInfo GetPeerInfo()
    {
        return new PeerInfo(new Uri(GetClientEndpoint().ToString()), getClientId(), true);
    }
}