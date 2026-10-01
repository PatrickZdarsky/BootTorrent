namespace btserver.Data;

public class ProxyDesignation
{
    public Guid ZoneId { get; set; }
    public required string ArtifactId { get; set; }
    public required string MachineId { get; set; }
    public DateTime SelectedAtUtc { get; set; }
    public required string Reason { get; set; }
}
