using boottorrent_lib.client;
using btserver.Config.Swarm;
using btserver.Swarm;

namespace btserver.Zone;

public class ZoneFactory
{
    public static IZone CreateZone(BaseZoneConfig zoneConfig)
    {
        return zoneConfig switch
        {
            StaticZoneConfig staticZoneConfig => new StaticZone(staticZoneConfig.Name, staticZoneConfig.MachineIds),
            SubnetZoneConfig subnetZoneConfig => new SubnetZone(subnetZoneConfig.Name, subnetZoneConfig.Subnet),
            _ => throw new ArgumentException($"Unknown zone config type: {zoneConfig.GetType().Name}")
        };
    }
}