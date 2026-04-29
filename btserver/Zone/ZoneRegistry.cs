using btserver.Config.Swarm;
using btserver.Swarm;
using Microsoft.Extensions.Options;

namespace btserver.Zone;

public class ZoneRegistry
{
    private readonly MachineRegistry _machineRegistry;
    private readonly IOptionsMonitor<SwarmConfig> _config;

    public List<IZone>? Zones { get; private set; }
    
    public event Action? ZonesUpdated;
    
    public ZoneRegistry(MachineRegistry machineRegistry, IOptionsMonitor<SwarmConfig> config)
    {
        _machineRegistry = machineRegistry;
        _config = config;
        _config.OnChange(_ => LoadZonesFromConfig());
        
        LoadZonesFromConfig();
    }
    
    private void LoadZonesFromConfig()
    {
        var newZones = _config.CurrentValue.Zones
            .Select(zoneConfig => ZoneFactory.CreateZone(zoneConfig, _machineRegistry.Machines.Values))
            .ToList();

        if (Zones is null || !newZones.SequenceEqual(Zones))
        {
            Zones = newZones;
            ZonesUpdated?.Invoke();
        }
    }
}