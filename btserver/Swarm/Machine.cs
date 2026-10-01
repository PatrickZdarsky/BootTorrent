namespace boottorrent_lib.client;

/// <summary>
/// A single individual machine.
/// </summary>
/// <param name="id"></param>
public class Machine(string id, string ipAddress)
{
    public string Id { get; } = id;
    public string IpAddress { get; set; } = ipAddress;
    
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    /// <summary>Whether this registration has received a heartbeat with artifact status.</summary>
    public bool HasReportedStatus { get; set; }
    public List<string> LoadedArtifacts { get; set; } = [];
    public List<string> KnownArtifacts { get; set; } = [];
    public Dictionary<string, double> PendingArtifacts { get; set; } = [];
    public string ReportedConfigHash { get; set; } = string.Empty;
    public string DesiredConfigHash { get; set; } = string.Empty;
    /// <summary>The acknowledged configuration hash for which missing assignments were last replayed.</summary>
    public string LastAssignmentReconciliationHash { get; set; } = string.Empty;
    public DateTime LastAssignmentReconciliationUtc { get; set; }
    public List<string> AssignedZones { get; set; } = [];
    public string? DistributionZoneId { get; set; }
    public List<string> ConflictingDistributionZoneIds { get; set; } = [];
}
