using System.Net;
using btserver.Swarm;
using btserver.torrent.tracker;
using btserver.Zone;

namespace btserver.torrent.impl;

/// <summary>Limits tracker peers to a client's distribution zone and exposes central seeders to designated proxies.</summary>
public sealed class ZoneTorrentAccessPolicy(
    ZoneManager zoneManager,
    ProxyElectionService election,
    ITorrentArtifactRegistry artifacts,
    CentralSeederPeerProvider centralSeeders,
    ILogger<ZoneTorrentAccessPolicy> logger) : ITorrentAccessPolicy
{
    /// <summary>Gets the policy's registry name.</summary>
    public string Name => "zone-aware";
    /// <summary>Gets the policy's registry priority.</summary>
    public int Priority => 100;

    /// <summary>Handles every announce as the default policy; invalid clients receive no peers during peer selection.</summary>
    /// <param name="request">The tracker announce request.</param>
    /// <param name="cancellationToken">Unused by this synchronous check.</param>
    /// <returns><see langword="true"/> for every request.</returns>
    public Task<bool> CanHandleAsync(TorrentPeerRequest request, CancellationToken cancellationToken = default) => Task.FromResult(true);

    /// <summary>Gets active local peers for an assigned client and adds central seeders when it is a designated proxy.</summary>
    /// <remarks>Unknown clients, ambiguous zone membership, and unassigned artifacts yield no peers. Results respect the announce peer limit.</remarks>
    /// <param name="request">The announce context, including the requesting machine and available peers.</param>
    /// <param name="cancellationToken">Cancels membership queries and election reconciliation.</param>
    /// <returns>The peers visible to this client.</returns>
    public async Task<IReadOnlyList<Peer>> GetPeersForAnnounceAsync(TorrentPeerRequest request, CancellationToken cancellationToken = default)
    {
        if (request.RequestingMachine is null) return [];
        var membership = await zoneManager.GetMembershipAsync(request.RequestingMachine, cancellationToken);
        if (membership.IsAmbiguous || !Guid.TryParse(membership.DistributionZoneId, out var zoneId))
        {
            logger.LogWarning("Cannot resolve one distribution zone for peer {PeerId}.", request.RequestingPeer.PeerId);
            return [];
        }

        var artifact = artifacts.GetArtifactByInfoHash(request.InfoHash);
        if (artifact is null) return [];
        if (!await election.IsMachineAssignedAsync(request.RequestingMachine, artifact.ID, cancellationToken)) return [];
        await election.ReconcileAsync(zoneId, artifact.ID, cancellationToken);

        var activeMemberships = await zoneManager.GetMembershipsAsync(request.ActiveMachines, cancellationToken);
        var eligibleAddresses = activeMemberships
            .Where(entry => entry.Value.DistributionZoneId == membership.DistributionZoneId)
            .Select(entry => entry.Key)
            .Where(machine => IPAddress.TryParse(machine.IpAddress, out _))
            .Select(machine => IPAddress.Parse(machine.IpAddress))
            .GroupBy(address => address)
            .Where(group => group.Count() == 1)
            .Select(group => group.Key)
            .ToHashSet();

        var peers = request.AvailablePeers
            .Where(peer => peer.PeerId != request.RequestingPeer.PeerId && eligibleAddresses.Contains(peer.EndPoint.Address))
            .OrderByDescending(peer => peer.LastSeen)
            .ToList();

        if (await election.IsDesignatedAsync(zoneId, artifact.ID, request.RequestingMachine.Id, cancellationToken))
        {
            var originPeers = await centralSeeders.GetPeersAsync(request.InfoHash, request.RequestingPeer, cancellationToken);
            // Reserve room for the origin even in a large local swarm.
            var origin = originPeers.Where(peer => peers.All(existing => !existing.EndPoint.Equals(peer.EndPoint)))
                .Take(request.MaxPeers).ToList();
            return origin.Concat(peers.Take(Math.Max(0, request.MaxPeers - origin.Count))).ToList();
        }

        return peers.Take(request.MaxPeers).ToList();
    }
}
