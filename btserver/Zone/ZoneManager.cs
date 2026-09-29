using boottorrent_lib.client;
using btserver.Data;
using Microsoft.EntityFrameworkCore;

namespace btserver.Zone;

public class ZoneManager(IServiceScopeFactory serviceScopeFactory)
{
    public async Task<List<string>> GetAssignedZoneIdsAsync(
        Machine machine,
        CancellationToken cancellationToken = default)
    {
        var zones = await LoadZonesAsync(cancellationToken);
        return GetAssignedZoneIds(machine, zones);
    }

    public async Task<IReadOnlyDictionary<Machine, List<string>>> GetAssignedZoneIdsAsync(
        IEnumerable<Machine> machines,
        CancellationToken cancellationToken = default)
    {
        var machineList = machines.ToList();
        if (machineList.Count == 0)
        {
            return new Dictionary<Machine, List<string>>();
        }

        var zones = await LoadZonesAsync(cancellationToken);
        return machineList.ToDictionary(machine => machine, machine => GetAssignedZoneIds(machine, zones));
    }

    private async Task<List<Zone>> LoadZonesAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BtDbContext>();
        return await dbContext.Zones.AsNoTracking().ToListAsync(cancellationToken);
    }

    private static List<string> GetAssignedZoneIds(Machine machine, IEnumerable<Zone> zones)
    {
        return zones
            .Where(zone => zone.Contains(machine))
            .Select(zone => zone.Id.ToString())
            .ToList();
    }
}
