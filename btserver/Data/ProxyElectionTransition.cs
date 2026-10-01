namespace btserver.Data;

public class ProxyElectionTransition
{
    public long Id { get; set; }
    public Guid ZoneId { get; set; }
    public required string ArtifactId { get; set; }
    public required string MachineId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public required string Action { get; set; }
    public required string Reason { get; set; }
    public long Generation { get; set; }
}
