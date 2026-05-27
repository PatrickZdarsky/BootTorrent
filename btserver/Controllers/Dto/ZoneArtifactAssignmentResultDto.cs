using Swashbuckle.AspNetCore.Annotations;

namespace btserver.Controllers.Dto;

[SwaggerSchema(Description = "Represents the artifact assignments for a zone.")]
public class ZoneArtifactAssignmentResultDto
{
    [SwaggerSchema(Description = "The zone identifier.")]
    public Guid ZoneId { get; set; }

    [SwaggerSchema(Description = "The artifact ids currently assigned to the zone.")]
    public List<string> AssignedArtifactIds { get; set; } = [];
}
