namespace btserver.Data;

public class ProxyElectionState
{
    public Guid ZoneId { get; set; }
    public required string ArtifactId { get; set; }
    public int AppliedInitialCount { get; set; }
    public bool Initialized { get; set; }
    public long Generation { get; set; }
}
