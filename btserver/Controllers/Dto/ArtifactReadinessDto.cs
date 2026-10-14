namespace btserver.Controllers.Dto;

public sealed class ArtifactReadinessDto
{
    public string ArtifactId { get; set; } = string.Empty;
    public bool Ready { get; set; }
    public string? Reason { get; set; }
    public bool TorrentMetadataAvailable { get; set; }
    public bool PayloadAvailable { get; set; }
    public bool SeederActive { get; set; }
    public string? SeederAddress { get; set; }
    public int? SeederPort { get; set; }
}
