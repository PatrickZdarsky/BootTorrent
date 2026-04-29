using boottorrent_lib.client;
using btserver.Config.Swarm;
using btserver.Swarm;

namespace btserver.Zone;

public class ZoneFactory
{
    public static IZone CreateZone(BaseZoneConfig zoneConfig, ICollection<Machine> machines)
    {
        return zoneConfig switch
        {
            StaticZoneConfig staticZoneConfig => new StaticZone(staticZoneConfig.Name, machines, staticZoneConfig.MachineIds),
            SubnetZoneConfig subnetZoneConfig => new SubnetZone(subnetZoneConfig.Name, machines, subnetZoneConfig.Subnet),
            _ => throw new ArgumentException($"Unknown zone config type: {zoneConfig.GetType().Name}")
        };
    }
}