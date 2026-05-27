using System.Collections.Concurrent;
using System.Net;
using boottorrent_lib.client;
using btserver.Config;
using btserver.Data;
using btserver.Swarm;
using btserver.torrent.tracker;
using btserver.Zone;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace btserver.torrent.impl;

public class SubnetZoneTorrentAccessPolicy : ITorrentAccessPolicy
{
    private readonly ISeederRegistry _seederRegistry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TorrentConfig _torrentConfig;
    private readonly ILogger<SubnetZoneTorrentAccessPolicy> _logger;
    private readonly ConcurrentDictionary<Guid, ProxyAssignment> _proxyAssignments = new();
    private readonly Lock _proxyCountLock = new();
    private int _proxyCount;

    public SubnetZoneTorrentAccessPolicy(
        ISeederRegistry seederRegistry,
        IServiceScopeFactory scopeFactory,
        IOptions<TorrentConfig> settings,
        ILogger<SubnetZoneTorrentAccessPolicy> logger,
        MachineRegistry machineRegistry,
        int proxyCount = 1)
    {
        _seederRegistry = seederRegistry;
        _scopeFactory = scopeFactory;
        _torrentConfig = settings.Value;
        _logger = logger;
        _proxyCount = Math.Max(1, proxyCount);

        machineRegistry.MachineStarted += OnMachineStarted;
        machineRegistry.MachineStopped += OnMachineStopped;
    }

    public string Name => "subnet-zone";

    public int Priority => 100;

    public void SetProxyCount(int proxyCount)
    {
        lock (_proxyCountLock)
        {
            _proxyCount = Math.Max(1, proxyCount);
        }
    }

    public async Task<bool> CanHandleAsync(TorrentPeerRequest request, CancellationToken cancellationToken = default)
    {
        if (request.RequestingMachine is null)
        {
            return false;
        }

        var subnetZone = await GetMatchingSubnetZoneAsync(request.RequestingMachine, cancellationToken);
        return subnetZone is not null;
    }

    public async Task<IReadOnlyList<Peer>> GetPeersForAnnounceAsync(TorrentPeerRequest request, CancellationToken cancellationToken = default)
    {
        if (request.RequestingMachine is null)
        {
            return [];
        }

        var subnetZone = await GetMatchingSubnetZoneAsync(request.RequestingMachine, cancellationToken);
        if (subnetZone is null)
        {
            return [];
        }

        var zoneMachines = subnetZone
            .Filter(request.ActiveMachines)
            .OrderBy(machine => machine.Id, StringComparer.Ordinal)
            .ToList();

        if (zoneMachines.Count == 0)
        {
            _proxyAssignments.TryRemove(subnetZone.Id, out _);
            return [];
        }

        var proxyMachineIds = GetOrUpdateProxyMachineIds(subnetZone, zoneMachines);

        var candidatePeers = request.AvailablePeers
            .Where(peer => peer.PeerId != request.RequestingPeer.PeerId)
            .ToList();

        var proxyPeersByMachineId = candidatePeers
            .Select(peer => new { Peer = peer, Machine = TryResolveMachine(peer.EndPoint.Address, request.ActiveMachines) })
            .Where(entry => entry.Machine is not null && proxyMachineIds.Contains(entry.Machine.Id))
            .GroupBy(entry => entry.Machine!.Id, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(entry => entry.Peer)
                    .OrderByDescending(peer => peer.LastSeen)
                    .First(),
                StringComparer.Ordinal);

        if (proxyMachineIds.Contains(request.RequestingMachine.Id))
        {
            var seederPeers = await GetCentralSeederPeersAsync(request.InfoHash, request.RequestingPeer, cancellationToken);
            _logger.LogDebug("Policy {PolicyName} marked machine {MachineId} as proxy in subnet zone {ZoneName} and returned {PeerCount} central seeder peers.", Name, request.RequestingMachine.Id, subnetZone.Name, seederPeers.Count);
            return seederPeers.Take(request.MaxPeers).ToList();
        }

        var proxyPeers = proxyMachineIds
            .Select(proxyMachineId => proxyPeersByMachineId.GetValueOrDefault(proxyMachineId))
            .Where(peer => peer is not null)
            .Cast<Peer>()
            .Take(request.MaxPeers)
            .ToList();

        if (proxyPeers.Count > 0)
        {
            _logger.LogDebug("Policy {PolicyName} returned {PeerCount} stable proxy peers for machine {MachineId} in subnet zone {ZoneName}.", Name, proxyPeers.Count, request.RequestingMachine.Id, subnetZone.Name);
            return proxyPeers;
        }

        var fallbackSeederPeers = await GetCentralSeederPeersAsync(request.InfoHash, request.RequestingPeer, cancellationToken);
        _logger.LogDebug("Policy {PolicyName} found no active proxy peers for machine {MachineId} in subnet zone {ZoneName}; returning {PeerCount} central seeder peers.", Name, request.RequestingMachine.Id, subnetZone.Name, fallbackSeederPeers.Count);
        return fallbackSeederPeers.Take(request.MaxPeers).ToList();
    }

    private IReadOnlyList<string> GetOrUpdateProxyMachineIds(SubnetZone subnetZone, List<Machine> zoneMachines)
    {
        int proxyCount;
        lock (_proxyCountLock)
        {
            proxyCount = _proxyCount;
        }

        var desiredCount = Math.Clamp(proxyCount, 1, zoneMachines.Count);
        var activeMachineIds = zoneMachines
            .Select(machine => machine.Id)
            .ToHashSet(StringComparer.Ordinal);

        var assignment = _proxyAssignments.GetOrAdd(subnetZone.Id, _ => new ProxyAssignment(subnetZone.Name));
        lock (assignment.SyncRoot)
        {
            assignment.ZoneName = subnetZone.Name;
            assignment.ProxyMachineIds.RemoveAll(machineId => !activeMachineIds.Contains(machineId));

            var assignedMachineIds = assignment.ProxyMachineIds.ToHashSet(StringComparer.Ordinal);
            foreach (var machine in zoneMachines)
            {
                if (assignment.ProxyMachineIds.Count >= desiredCount)
                {
                    break;
                }

                if (assignedMachineIds.Add(machine.Id))
                {
                    assignment.ProxyMachineIds.Add(machine.Id);
                }
            }

            if (assignment.ProxyMachineIds.Count > desiredCount)
            {
                assignment.ProxyMachineIds.RemoveRange(desiredCount, assignment.ProxyMachineIds.Count - desiredCount);
            }

            return assignment.ProxyMachineIds.ToList();
        }
    }

    private async Task<SubnetZone?> GetMatchingSubnetZoneAsync(Machine machine, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BtDbContext>();

        var zones = await dbContext.SubnetZones
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return zones.FirstOrDefault(zone => zone.Contains(machine));
    }

    private async Task<List<Peer>> GetCentralSeederPeersAsync(string infoHash, Peer requestingPeer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var seeders = await _seederRegistry.GetSeedersForTorrent(infoHash);
        var seederPeers = new List<Peer>();

        foreach (var seeder in seeders)
        {
            var endpoint = TryResolveSeederEndpoint(seeder.GetClientEndpoint());
            if (endpoint is null)
            {
                continue;
            }

            if (endpoint.Address.Equals(requestingPeer.EndPoint.Address) && endpoint.Port == requestingPeer.EndPoint.Port)
            {
                continue;
            }

            seederPeers.Add(new Peer(seeder.getClientId(), endpoint, 0, 0, 0));
        }

        return seederPeers
            .DistinctBy(peer => (peer.EndPoint.Address, peer.EndPoint.Port, peer.PeerId))
            .ToList();
    }

    private IPEndPoint? TryResolveSeederEndpoint(IPEndPoint endpoint)
    {
        if (endpoint.Address != IPAddress.Any && endpoint.Address != IPAddress.IPv6Any)
        {
            return endpoint;
        }

        if (!Uri.TryCreate(_torrentConfig.TrackerUrl, UriKind.Absolute, out var trackerUri))
        {
            _logger.LogWarning("TrackerUrl '{TrackerUrl}' could not be parsed while resolving central seeder endpoint.", _torrentConfig.TrackerUrl);
            return null;
        }

        if (IPAddress.TryParse(trackerUri.Host, out var ipAddress))
        {
            return new IPEndPoint(ipAddress, endpoint.Port);
        }

        try
        {
            var resolvedAddress = Dns.GetHostAddresses(trackerUri.Host)
                .FirstOrDefault(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

            return resolvedAddress is null ? null : new IPEndPoint(resolvedAddress, endpoint.Port);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve central seeder endpoint for host {TrackerHost}.", trackerUri.Host);
            return null;
        }
    }

    private void OnMachineStarted(object? sender, MachineRegistryEventArgs eventArgs)
    {
        _logger.LogTrace("Machine {MachineId} started. Stable proxy assignments remain unchanged until a zone needs new proxies.", eventArgs.Machine.Id);
    }

    private void OnMachineStopped(object? sender, MachineRegistryEventArgs eventArgs)
    {
        foreach (var assignmentEntry in _proxyAssignments)
        {
            lock (assignmentEntry.Value.SyncRoot)
            {
                assignmentEntry.Value.ProxyMachineIds.RemoveAll(machineId => machineId == eventArgs.Machine.Id);
            }
        }

        _logger.LogDebug("Removed machine {MachineId} from stable proxy assignments due to {StopReason}.", eventArgs.Machine.Id, eventArgs.StopReason);
    }

    private static Machine? TryResolveMachine(IPAddress ipAddress, IEnumerable<Machine> machines)
    {
        return machines.FirstOrDefault(machine =>
            IPAddress.TryParse(machine.IpAddress, out var machineAddress) &&
            machineAddress.Equals(ipAddress));
    }

    private sealed class ProxyAssignment
    {
        public ProxyAssignment(string zoneName)
        {
            ZoneName = zoneName;
        }

        public object SyncRoot { get; } = new();

        public string ZoneName { get; set; }

        public List<string> ProxyMachineIds { get; } = [];
    }
}
