using System.Collections.Concurrent;
using System.Net;
using boottorrent_lib.client;
using btserver.Swarm;
using btserver.torrent.tracker;

namespace btserver.torrent.impl;

public class TorrentAccessPolicyRegistry : ITorrentAccessPolicyRegistry
{
    private readonly ConcurrentDictionary<string, ITorrentAccessPolicy> _policies = new(StringComparer.Ordinal);
    private readonly MachineRegistry _machineRegistry;
    private readonly ILogger<TorrentAccessPolicyRegistry> _logger;

    public TorrentAccessPolicyRegistry(
        IEnumerable<ITorrentAccessPolicy> policies,
        MachineRegistry machineRegistry,
        ILogger<TorrentAccessPolicyRegistry> logger)
    {
        _machineRegistry = machineRegistry;
        _logger = logger;

        foreach (var policy in policies)
        {
            RegisterPolicy(policy);
        }
    }

    public IReadOnlyCollection<ITorrentAccessPolicy> GetPolicies()
    {
        return _policies.Values
            .OrderByDescending(policy => policy.Priority)
            .ThenBy(policy => policy.Name, StringComparer.Ordinal)
            .ToList();
    }

    public bool TryGetPolicy(string policyName, out ITorrentAccessPolicy? policy)
    {
        var found = _policies.TryGetValue(policyName, out var existingPolicy);
        policy = existingPolicy;
        return found;
    }

    public void RegisterPolicy(ITorrentAccessPolicy policy)
    {
        _policies[policy.Name] = policy;
        _logger.LogInformation("Registered torrent access policy {PolicyName} with priority {Priority}.", policy.Name, policy.Priority);
    }

    public bool UnregisterPolicy(string policyName)
    {
        var removed = _policies.TryRemove(policyName, out _);
        if (removed)
        {
            _logger.LogInformation("Unregistered torrent access policy {PolicyName}.", policyName);
        }

        return removed;
    }

    public async Task<IReadOnlyList<Peer>> GetPeersForAnnounceAsync(TorrentPeerRequest request, CancellationToken cancellationToken = default)
    {
        var activeMachines = _machineRegistry.Machines.Values.ToList();
        var requestingMachine = TryResolveMachine(request.RequestingPeer.EndPoint.Address, activeMachines);
        var enrichedRequest = request with
        {
            RequestingMachine = requestingMachine,
            ActiveMachines = activeMachines
        };

        foreach (var policy in GetPolicies())
        {
            if (!await policy.CanHandleAsync(enrichedRequest, cancellationToken))
            {
                continue;
            }

            _logger.LogDebug("Using torrent access policy {PolicyName} for peer {PeerId}.", policy.Name, enrichedRequest.RequestingPeer.PeerId);
            return await policy.GetPeersForAnnounceAsync(enrichedRequest, cancellationToken);
        }

        _logger.LogWarning("No torrent access policy matched peer {PeerId}; returning an empty peer list.", enrichedRequest.RequestingPeer.PeerId);
        return [];
    }

    private static Machine? TryResolveMachine(IPAddress ipAddress, IEnumerable<Machine> machines)
    {
        return machines.FirstOrDefault(machine =>
            IPAddress.TryParse(machine.IpAddress, out var machineAddress) &&
            machineAddress.Equals(ipAddress));
    }
}
