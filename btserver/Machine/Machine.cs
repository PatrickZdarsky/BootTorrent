namespace btserver.Machine;

public class Machine(string id, string ipAddress)
{
    public string Id { get; } = id;
    public string IpAddress { get; } = ipAddress;
    
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}