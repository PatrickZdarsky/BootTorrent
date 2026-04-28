namespace boottorrent_lib.client;

/// <summary>
/// The configuration of a machine with the zones it belongs to, and any additional information that the server wants to send to the client.
/// </summary>
public class MachineConfiguration
{
    public string ConfigHash { get; set; } = string.Empty;
    
    public List<string> AssignedZones { get; set; }
}