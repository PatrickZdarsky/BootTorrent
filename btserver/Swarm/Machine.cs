namespace boottorrent_lib.client;

/// <summary>
/// A single individual machine.
/// </summary>
/// <param name="id"></param>
public class Machine(string id, string ipAddress)
{
    public string Id { get; } = id;
    public string IpAddress { get; } = ipAddress;
    
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public List<string> LoadedArtifacts { get; set; }
    public Dictionary<string, double> PendingArtifacts { get; set; }
}