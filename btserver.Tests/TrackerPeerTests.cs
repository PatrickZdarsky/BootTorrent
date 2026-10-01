using System.Net;
using btserver.torrent.tracker;

namespace btserver.Tests;

public class TrackerPeerTests
{
    [Fact]
    public void ExpiredPeersAreRemovedWhileRecentlyAnnouncedPeersRemain()
    {
        var torrent = new Torrent("hash");
        torrent.UpdatePeer(new Peer("stale", new IPEndPoint(IPAddress.Parse("10.0.0.1"), 5000), 0, 0, 1)
        {
            LastSeen = DateTime.UtcNow.AddMinutes(-5)
        });
        torrent.UpdatePeer(new Peer("fresh", new IPEndPoint(IPAddress.Parse("10.0.0.2"), 5000), 0, 0, 1));

        var peers = torrent.GetActivePeers(TimeSpan.FromMinutes(2));

        Assert.Equal("fresh", Assert.Single(peers).PeerId);
        Assert.False(torrent.Peers.ContainsKey("stale"));
    }
}
