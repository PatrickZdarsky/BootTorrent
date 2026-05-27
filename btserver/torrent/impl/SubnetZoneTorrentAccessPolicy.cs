using System.Net;
using boottorrent_lib.client;
using btserver.Config;
using btserver.Data;
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
    private readonly Lock _proxyCountLock = new();
    private int _proxyCount;

    public SubnetZoneTorrentAccessPolicy(
        ISeederRegistry seederRegistry,
        IServiceScopeFactory scopeFactory,
        IOptions<TorrentConfig> settings,
        ILogger<SubnetZoneTorrentAccessPolicy> logger,
        int proxyCount = 1)
    {
        _seederRegistry = seederRegistry;
        _scopeFactory = scopeFactory;
        _torrentConfig = settings.Value;
        _logger = logger;
        _proxyCount = Math.Max(1, proxyCount);
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
            return [];
        }

        int proxyCount;
        lock (_proxyCountLock)
        {
            proxyCount = _proxyCount;
        }

        var proxyMachineIds = zoneMachines
            .Take(Math.Clamp(proxyCount, 1, zoneMachines.Count))
            .Select(machine => machine.Id)
            .ToHashSet(StringComparer.Ordinal);

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
            _logger.LogDebug("Policy {PolicyName} selected proxy machine {MachineId} in subnet zone {ZoneName} and returned {PeerCount} central seeder peers.", Name, request.RequestingMachine.Id, subnetZone.Name, seederPeers.Count);
            return seederPeers.Take(request.MaxPeers).ToList();
        }

        var proxyPeers = zoneMachines
            .Where(machine => proxyMachineIds.Contains(machine.Id))
            .Select(machine => proxyPeersByMachineId.GetValueOrDefault(machine.Id))
            .Where(peer => peer is not null)
            .Cast<Peer>()
            .Take(request.MaxPeers)
            .ToList();

        if (proxyPeers.Count > 0)
        {
            _logger.LogDebug("Policy {PolicyName} selected subnet zone {ZoneName} for machine {MachineId} and returned {PeerCount} proxy peers.", Name, subnetZone.Name, request.RequestingMachine.Id, proxyPeers.Count);
            return proxyPeers;
        }

        var fallbackSeederPeers = await GetCentralSeederPeersAsync(request.InfoHash, request.RequestingPeer, cancellationToken);
        _logger.LogDebug("Policy {PolicyName} found no active proxy peers for machine {MachineId} in subnet zone {ZoneName}; returning {PeerCount} central seeder peers.", Name, request.RequestingMachine.Id, subnetZone.Name, fallbackSeederPeers.Count);
        return fallbackSeederPeers.Take(request.MaxPeers).ToList();
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

    private static Machine? TryResolveMachine(IPAddress ipAddress, IEnumerable<Machine> machines)
    {
        return machines.FirstOrDefault(machine =>
            IPAddress.TryParse(machine.IpAddress, out var machineAddress) &&
            machineAddress.Equals(ipAddress));
    }
}
