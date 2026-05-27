using System.Collections.Concurrent;
using System.Net;
using boottorrent_lib.client;
using btserver.Config;
using btserver.Controllers.Dto;
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
    private readonly int _defaultProxyCount;

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
        _defaultProxyCount = Math.Max(1, proxyCount);

        machineRegistry.MachineStarted += OnMachineStarted;
        machineRegistry.MachineStopped += OnMachineStopped;
    }

    public string Name => "subnet-zone";

    public int Priority => 100;

    public async Task<SubnetZonePolicyDto> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BtDbContext>();

        var zones = await dbContext.SubnetZones
            .AsNoTracking()
            .OrderBy(zone => zone.Name)
            .ToListAsync(cancellationToken);

        var savedConfigurations = await dbContext.SubnetZonePolicyConfigurations
            .AsNoTracking()
            .ToDictionaryAsync(configuration => configuration.ZoneId, cancellationToken);

        return new SubnetZonePolicyDto
        {
            Name = Name,
            Priority = Priority,
            Zones = zones.Select(zone =>
            {
                var configuration = savedConfigurations.GetValueOrDefault(zone.Id) ?? CreateDefaultConfiguration(zone.Id);
                var assignment = _proxyAssignments.GetValueOrDefault(zone.Id);
                var proxyMachineIds = assignment?.SnapshotProxyMachineIds() ?? configuration.ProxyMachineIds;

                return new SubnetZonePolicyZoneConfigurationDto
                {
                    ZoneId = zone.Id,
                    ZoneName = zone.Name,
                    ProxyCount = configuration.ProxyCount,
                    ProxyMachineIds = proxyMachineIds
                };
            }).ToList()
        };
    }

    public async Task<SubnetZonePolicyZoneConfigurationDto?> GetZoneConfigurationAsync(Guid zoneId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BtDbContext>();

        var zone = await dbContext.SubnetZones
            .AsNoTracking()
            .FirstOrDefaultAsync(existingZone => existingZone.Id == zoneId, cancellationToken);

        if (zone is null)
        {
            return null;
        }

        var configuration = await dbContext.SubnetZonePolicyConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(existingConfiguration => existingConfiguration.ZoneId == zoneId, cancellationToken)
            ?? CreateDefaultConfiguration(zoneId);

        var assignment = _proxyAssignments.GetValueOrDefault(zoneId);
        return new SubnetZonePolicyZoneConfigurationDto
        {
            ZoneId = zone.Id,
            ZoneName = zone.Name,
            ProxyCount = configuration.ProxyCount,
            ProxyMachineIds = assignment?.SnapshotProxyMachineIds() ?? configuration.ProxyMachineIds
        };
    }

    public async Task<SubnetZonePolicyZoneConfigurationDto?> UpsertZoneConfigurationAsync(
        Guid zoneId,
        int proxyCount,
        IEnumerable<string>? proxyMachineIds,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BtDbContext>();

        var zone = await dbContext.SubnetZones
            .FirstOrDefaultAsync(existingZone => existingZone.Id == zoneId, cancellationToken);

        if (zone is null)
        {
            return null;
        }

        var normalizedProxyMachineIds = (proxyMachineIds ?? [])
            .Where(machineId => !string.IsNullOrWhiteSpace(machineId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var configuration = await dbContext.SubnetZonePolicyConfigurations
            .FirstOrDefaultAsync(existingConfiguration => existingConfiguration.ZoneId == zoneId, cancellationToken);

        if (configuration is null)
        {
            configuration = new SubnetZonePolicyConfiguration
            {
                ZoneId = zoneId
            };
            dbContext.SubnetZonePolicyConfigurations.Add(configuration);
        }

        configuration.ProxyCount = Math.Max(1, proxyCount);
        configuration.ProxyMachineIds = normalizedProxyMachineIds;
        await dbContext.SaveChangesAsync(cancellationToken);

        var assignment = _proxyAssignments.AddOrUpdate(
            zoneId,
            _ => new ProxyAssignment(zone.Name, configuration.ProxyMachineIds),
            (_, existingAssignment) =>
            {
                lock (existingAssignment.SyncRoot)
                {
                    existingAssignment.ZoneName = zone.Name;
                    existingAssignment.ReplaceProxyMachineIds(configuration.ProxyMachineIds);
                }

                return existingAssignment;
            });

        return new SubnetZonePolicyZoneConfigurationDto
        {
            ZoneId = zoneId,
            ZoneName = zone.Name,
            ProxyCount = configuration.ProxyCount,
            ProxyMachineIds = assignment.SnapshotProxyMachineIds()
        };
    }

    public async Task<bool> DeleteZoneConfigurationAsync(Guid zoneId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BtDbContext>();

        var configuration = await dbContext.SubnetZonePolicyConfigurations
            .FirstOrDefaultAsync(existingConfiguration => existingConfiguration.ZoneId == zoneId, cancellationToken);

        _proxyAssignments.TryRemove(zoneId, out _);

        if (configuration is null)
        {
            return false;
        }

        dbContext.SubnetZonePolicyConfigurations.Remove(configuration);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
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

        var proxyMachineIds = await GetOrUpdateProxyMachineIdsAsync(subnetZone, zoneMachines, cancellationToken);

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

    private async Task<IReadOnlyList<string>> GetOrUpdateProxyMachineIdsAsync(SubnetZone subnetZone, List<Machine> zoneMachines, CancellationToken cancellationToken)
    {
        var configuration = await GetOrCreateZoneConfigurationAsync(subnetZone, cancellationToken);
        var desiredCount = Math.Clamp(configuration.ProxyCount, 1, zoneMachines.Count);
        var activeMachineIds = zoneMachines
            .Select(machine => machine.Id)
            .ToHashSet(StringComparer.Ordinal);

        var assignment = _proxyAssignments.GetOrAdd(
            subnetZone.Id,
            _ => new ProxyAssignment(subnetZone.Name, configuration.ProxyMachineIds));

        var changed = false;
        List<string> proxyMachineIds;
        lock (assignment.SyncRoot)
        {
            assignment.ZoneName = subnetZone.Name;
            changed = assignment.ProxyMachineIds.RemoveAll(machineId => !activeMachineIds.Contains(machineId)) > 0;

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
                    changed = true;
                }
            }

            if (assignment.ProxyMachineIds.Count > desiredCount)
            {
                assignment.ProxyMachineIds.RemoveRange(desiredCount, assignment.ProxyMachineIds.Count - desiredCount);
                changed = true;
            }

            proxyMachineIds = assignment.ProxyMachineIds.ToList();
        }

        if (changed || !ProxyMachineIdsMatch(configuration.ProxyMachineIds, proxyMachineIds))
        {
            await SaveZoneConfigurationAsync(subnetZone.Id, configuration.ProxyCount, proxyMachineIds, cancellationToken);
        }

        return proxyMachineIds;
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

    private async Task<SubnetZonePolicyConfiguration> GetOrCreateZoneConfigurationAsync(SubnetZone subnetZone, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BtDbContext>();

        var configuration = await dbContext.SubnetZonePolicyConfigurations
            .FirstOrDefaultAsync(existingConfiguration => existingConfiguration.ZoneId == subnetZone.Id, cancellationToken);

        if (configuration is not null)
        {
            return configuration;
        }

        configuration = CreateDefaultConfiguration(subnetZone.Id);
        dbContext.SubnetZonePolicyConfigurations.Add(configuration);
        await dbContext.SaveChangesAsync(cancellationToken);
        return configuration;
    }

    private async Task SaveZoneConfigurationAsync(Guid zoneId, int proxyCount, IReadOnlyCollection<string> proxyMachineIds, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BtDbContext>();

        var configuration = await dbContext.SubnetZonePolicyConfigurations
            .FirstOrDefaultAsync(existingConfiguration => existingConfiguration.ZoneId == zoneId, cancellationToken);

        if (configuration is null)
        {
            configuration = new SubnetZonePolicyConfiguration
            {
                ZoneId = zoneId
            };
            dbContext.SubnetZonePolicyConfigurations.Add(configuration);
        }

        configuration.ProxyCount = Math.Max(1, proxyCount);
        configuration.ProxyMachineIds = proxyMachineIds.ToList();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private SubnetZonePolicyConfiguration CreateDefaultConfiguration(Guid zoneId)
    {
        return new SubnetZonePolicyConfiguration
        {
            ZoneId = zoneId,
            ProxyCount = _defaultProxyCount,
            ProxyMachineIds = []
        };
    }

    private static bool ProxyMachineIdsMatch(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.SequenceEqual(right);
    }

    private sealed class ProxyAssignment
    {
        public ProxyAssignment(string zoneName, IEnumerable<string>? proxyMachineIds = null)
        {
            ZoneName = zoneName;
            ProxyMachineIds = (proxyMachineIds ?? [])
                .Where(machineId => !string.IsNullOrWhiteSpace(machineId))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        public object SyncRoot { get; } = new();

        public string ZoneName { get; set; }

        public List<string> ProxyMachineIds { get; }

        public List<string> SnapshotProxyMachineIds()
        {
            lock (SyncRoot)
            {
                return ProxyMachineIds.ToList();
            }
        }

        public void ReplaceProxyMachineIds(IEnumerable<string> proxyMachineIds)
        {
            ProxyMachineIds.Clear();
            ProxyMachineIds.AddRange(proxyMachineIds
                .Where(machineId => !string.IsNullOrWhiteSpace(machineId))
                .Distinct(StringComparer.Ordinal));
        }
    }
}
