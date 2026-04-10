using System.Net;

namespace btserver.torrent.tracker;

public class Peer
{
    public string PeerId { get; }
    public IPEndPoint EndPoint { get; }
    public long Uploaded { get; set; }
    public long Downloaded { get; set; }
    public long Left { get; set; }
    public DateTime LastSeen { get; set; }

    public Peer(string peerId, IPEndPoint endPoint, long uploaded, long downloaded, long left)
    {
        PeerId = peerId;
        EndPoint = endPoint;
        Uploaded = uploaded;
        Downloaded = downloaded;
        Left = left;
        LastSeen = DateTime.UtcNow;
    }
}

