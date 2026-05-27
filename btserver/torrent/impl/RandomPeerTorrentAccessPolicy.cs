using btserver.torrent.tracker;

namespace btserver.torrent.impl;

public class RandomPeerTorrentAccessPolicy : ITorrentAccessPolicy
{
    public string Name => "random-fallback";

    public int Priority => int.MinValue;

    public Task<bool> CanHandleAsync(TorrentPeerRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<Peer>> GetPeersForAnnounceAsync(TorrentPeerRequest request, CancellationToken cancellationToken = default)
    {
        var peers = request.AvailablePeers
            .Where(peer => peer.PeerId != request.RequestingPeer.PeerId)
            .OrderBy(_ => Random.Shared.Next())
            .Take(request.MaxPeers)
            .ToList();

        return Task.FromResult<IReadOnlyList<Peer>>(peers);
    }
}
