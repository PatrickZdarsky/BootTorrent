using boottorrent_lib.client;
using btserver.Data;
using Microsoft.EntityFrameworkCore;

namespace btserver.Zone;

/// <summary>Resolved management and distribution zone membership for one machine.</summary>
/// <param name="ManagementZoneIds">IDs of all matching zones, including the default zone when applicable.</param>
/// <param name="DistributionZoneId">The sole distribution zone ID, or <see langword="null"/> when membership is ambiguous.</param>
/// <param name="ConflictingDistributionZoneIds">IDs of overlapping distribution zones.</param>
public sealed record ZoneMembership(
    List<string> ManagementZoneIds,
    string? DistributionZoneId,
    List<string> ConflictingDistributionZoneIds)
{
    /// <summary>Whether more than one distribution zone matches the machine.</summary>
    public bool IsAmbiguous => ConflictingDistributionZoneIds.Count > 0;
}

/// <summary>Resolves machines into overlapping management zones and one distribution zone.</summary>
public class ZoneManager(IServiceScopeFactory serviceScopeFactory)
{
    /// <summary>ID of the logical catch-all distribution zone for machines outside explicit distribution zones.</summary>
    public static readonly Guid DefaultDistributionZoneId = Guid.Parse("7b0a6598-24e7-43a1-844a-a068ce5d19da");

    /// <summary>Loads configured zones and resolves one machine's membership.</summary>
    /// <param name="machine">The machine to classify.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns>The resolved membership.</returns>
    public async Task<ZoneMembership> GetMembershipAsync(
        Machine machine,
        CancellationToken cancellationToken = default)
    {
        var zones = await LoadZonesAsync(cancellationToken);
        return Resolve(machine, zones);
    }

    /// <summary>Resolves a collection of machines using one snapshot of configured zones.</summary>
    /// <param name="machines">The machines to classify.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns>A membership for each supplied machine.</returns>
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

    /// <summary>Resolves membership from the supplied zones without querying the database.</summary>
    /// <remarks>Management zones may overlap. No distribution match selects the default zone; multiple matches leave the distribution zone unresolved.</remarks>
    /// <param name="machine">The machine to classify.</param>
    /// <param name="zones">The zones to test against the machine.</param>
    /// <returns>The resolved membership and any distribution conflicts.</returns>
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
