using Swashbuckle.AspNetCore.Annotations;

namespace btserver.Controllers.Dto;

[SwaggerSchema(Description = "Represents a managed torrent artifact.")]
public class ArtifactDto
{
    [SwaggerSchema(Description = "The artifact identifier.")]
    public string Id { get; set; } = string.Empty;

    [SwaggerSchema(Description = "The artifact display name.")]
    public string Name { get; set; } = string.Empty;

    [SwaggerSchema(Description = "The V1 info hash.")]
    public string InfoHashV1 { get; set; } = string.Empty;

    [SwaggerSchema(Description = "The V2 info hash.")]
    public string InfoHashV2 { get; set; } = string.Empty;

    [SwaggerSchema(Description = "The URL clients can use to fetch the torrent file.")]
    public string TorrentFileUrl { get; set; } = string.Empty;

    [SwaggerSchema(Description = "Zone ids currently assigned to this artifact.")]
    public List<Guid> AssignedZoneIds { get; set; } = [];
}
