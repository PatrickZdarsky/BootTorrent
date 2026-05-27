using btserver.torrent.tracker;

namespace btserver.torrent;

public interface ITorrentAccessPolicyRegistry
{
    IReadOnlyCollection<ITorrentAccessPolicy> GetPolicies();

    bool TryGetPolicy(string policyName, out ITorrentAccessPolicy? policy);

    void RegisterPolicy(ITorrentAccessPolicy policy);

    bool UnregisterPolicy(string policyName);

    Task<IReadOnlyList<Peer>> GetPeersForAnnounceAsync(TorrentPeerRequest request, CancellationToken cancellationToken = default);
}
