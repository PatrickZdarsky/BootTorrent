using System.Collections.Concurrent;
using System.Collections.Generic;

namespace btserver.torrent.impl;

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
}

