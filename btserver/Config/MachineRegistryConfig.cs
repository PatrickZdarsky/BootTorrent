namespace btserver.Config;

public class MachineRegistryConfig
{
    public int HeartbeatTimeoutSeconds { get; set; } = 90;

    public int HeartbeatCheckIntervalSeconds { get; set; } = 15;
}
