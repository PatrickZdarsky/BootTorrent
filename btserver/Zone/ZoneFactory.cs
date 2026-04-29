using boottorrent_lib.client;
using btserver.Config.Swarm;
using btserver.Swarm;

namespace btserver.Zone;

public class ZoneFactory
{
    public static Zone CreateZone(BaseZoneConfig zoneConfig)
    {
        return zoneConfig switch
        {
            StaticZoneConfig staticZoneConfig => new StaticZone(staticZoneConfig.MachineIds)
                { Name = staticZoneConfig.Name, AssignedArtifactIds = staticZoneConfig.AssignedArtifactIds },
            SubnetZoneConfig subnetZoneConfig => new SubnetZone(subnetZoneConfig.Subnet)
                { Name = subnetZoneConfig.Name, AssignedArtifactIds = subnetZoneConfig.AssignedArtifactIds },
            _ => throw new ArgumentException($"Unknown zone config type: {zoneConfig.GetType().Name}")
        };
    }
}