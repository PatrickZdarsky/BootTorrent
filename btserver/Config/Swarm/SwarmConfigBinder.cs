namespace btserver.Config.Swarm;

public class SwarmConfigBinder
{
    public static BaseZoneConfig BindZone(IConfigurationSection section)
    {
        var type = section["type"]?.Trim().ToLowerInvariant();

        BaseZoneConfig zone = type switch
        {
            "static" => new StaticZoneConfig(),
            "subnet"  => new SubnetZoneConfig(),
            null or "" => throw new InvalidOperationException(
                $"Missing zone type at '{section.Path}'."),

            _ => throw new InvalidOperationException(
                $"Unknown zone type '{type}' at '{section.Path}'.")
        };

        section.Bind(zone);
        return zone;
    }
}