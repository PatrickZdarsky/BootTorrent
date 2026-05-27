using boottorrent_lib.client;
using btserver.torrent.tracker;

namespace btserver.torrent;

public interface ITorrentAccessPolicy
{
    string Name { get; }

    int Priority { get; }

    /// <summary>
    /// Determines whether this policy should be used for the current requesting machine and announce context.
    /// </summary>
    Task<bool> CanHandleAsync(TorrentPeerRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects the peers which should be returned to a requesting BitTorrent client for a tracker announce.
    /// </summary>
    Task<IReadOnlyList<Peer>> GetPeersForAnnounceAsync(TorrentPeerRequest request, CancellationToken cancellationToken = default);
}

public sealed record TorrentPeerRequest(
    string InfoHash,
    Peer RequestingPeer,
    IReadOnlyCollection<Peer> AvailablePeers,
    int MaxPeers,
    Machine? RequestingMachine,
    IReadOnlyCollection<Machine> ActiveMachines);
