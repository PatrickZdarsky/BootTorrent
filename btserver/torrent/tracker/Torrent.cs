using System.Collections.Concurrent;

namespace btserver.torrent.tracker;

public class Torrent
{
    public string InfoHash { get; }
    public ConcurrentDictionary<string, Peer> Peers { get; } = new();

    public Torrent(string infoHash)
    {
        InfoHash = infoHash;
    }

    public void UpdatePeer(Peer peer)
    {
        Peers.AddOrUpdate(peer.PeerId, peer, (key, existingPeer) =>
        {
            existingPeer.Uploaded = peer.Uploaded;
            existingPeer.Downloaded = peer.Downloaded;
            existingPeer.Left = peer.Left;
            existingPeer.LastSeen = peer.LastSeen;
            return existingPeer;
        });
    }

    public void RemovePeer(string peerId)
    {
        Peers.TryRemove(peerId, out _);
    }

    /// <summary>Removes expired tracker peers and returns a snapshot of peers seen within the expiration window.</summary>
    /// <param name="expiration">Maximum time since a peer's last announce.</param>
    /// <returns>Peers whose last announce is within the window.</returns>
    public List<Peer> GetActivePeers(TimeSpan expiration)
    {
        var threshold = DateTime.UtcNow - expiration;
        foreach (var (peerId, peer) in Peers)
        {
            if (peer.LastSeen < threshold) Peers.TryRemove(peerId, out _);
        }
        return Peers.Values.Where(peer => peer.LastSeen >= threshold).ToList();
    }

    public List<Peer> GetPeers(int count)
    {
        var peerList = new List<Peer>(Peers.Values);
        // Simple random shuffle
        var random = new Random();
        for (int i = 0; i < peerList.Count; i++)
        {
            int j = random.Next(i, peerList.Count);
            (peerList[i], peerList[j]) = (peerList[j], peerList[i]);
        }
        return peerList.Take(count).ToList();
    }
    
    public int CompletedPeers => Peers.Values.Count(p => p.Left == 0);
    public int IncompletePeers => Peers.Values.Count(p => p.Left > 0);
}
