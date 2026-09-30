using boottorrent_lib.client;
using btserver.Data;
using Microsoft.EntityFrameworkCore;

namespace btserver.Zone;

public sealed record ZoneMembership(
    List<string> ManagementZoneIds,
    string? DistributionZoneId,
    List<string> ConflictingDistributionZoneIds)
{
    public bool IsAmbiguous => ConflictingDistributionZoneIds.Count > 0;
}

public class ZoneManager(IServiceScopeFactory serviceScopeFactory)
{
    // A logical catch-all zone. Membership is calculated rather than stored as machine IDs.
    public static readonly Guid DefaultDistributionZoneId = Guid.Parse("7b0a6598-24e7-43a1-844a-a068ce5d19da");

    public async Task<ZoneMembership> GetMembershipAsync(
        Machine machine,
        CancellationToken cancellationToken = default)
    {
        var zones = await LoadZonesAsync(cancellationToken);
        return Resolve(machine, zones);
    }

    public async Task<IReadOnlyDictionary<Machine, ZoneMembership>> GetMembershipsAsync(
        IEnumerable<Machine> machines,
        CancellationToken cancellationToken = default)
    {
        var machineList = machines.ToList();
        if (machineList.Count == 0)
        {
            return new Dictionary<Machine, ZoneMembership>();
        }

        var zones = await LoadZonesAsync(cancellationToken);
        return machineList.ToDictionary(machine => machine, machine => Resolve(machine, zones));
    }

    public static ZoneMembership Resolve(Machine machine, IEnumerable<Zone> zones)
    {
        var matches = zones.Where(zone => zone.Contains(machine)).ToList();
        var distributionMatches = matches.Where(zone => zone.IsDistributionZone).ToList();
        var managementZoneIds = matches.Select(zone => zone.Id.ToString()).ToList();

        if (distributionMatches.Count == 0)
        {
            var defaultId = DefaultDistributionZoneId.ToString();
            managementZoneIds.Add(defaultId);
            return new ZoneMembership(managementZoneIds, defaultId, []);
        }

        if (distributionMatches.Count == 1)
        {
            return new ZoneMembership(managementZoneIds, distributionMatches[0].Id.ToString(), []);
        }

        return new ZoneMembership(
            managementZoneIds,
            null,
            distributionMatches.Select(zone => zone.Id.ToString()).Order(StringComparer.Ordinal).ToList());
    }

    private async Task<List<Zone>> LoadZonesAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BtDbContext>();
        return await dbContext.Zones.AsNoTracking().ToListAsync(cancellationToken);
    }
}
