namespace btserver.Config.Swarm;

public sealed class StaticZoneConfig : BaseZoneConfig
{
    public List<string> MachineIds { get; set; } = [];
}