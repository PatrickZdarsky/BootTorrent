using boottorrent_lib.client;
using btserver.torrent;
using btserver.Zone;

namespace btserver.Swarm;

public class ArtifactAssigner(ITorrentArtifactRegistry artifactRegistry, MachineRegistry machineRegistry, ZoneRegistry zoneRegistry)
{
    private async Task Process(Machine machine)
    {
        var assignedArtifacts = GetAllAssignedArtifactIds(machine);
        
    }
    
    private List<string> GetAllAssignedArtifactIds(Machine machine)
    {
        return zoneRegistry.Zones?
            .Where(zone => zone.Contains(machine))
            .SelectMany(zone => zone.AssignedArtifactIds)
            .Distinct()
            .ToList() ?? [];
    }
}