namespace btserver.Config.Swarm;

public abstract class BaseZoneConfig
{
    public string Name { get; set; }
    public string Type { get; set; }
    public List<string> AssignedArtifactIds { get; set; } = [];
}