using Swashbuckle.AspNetCore.Annotations;

namespace btserver.Controllers.Dto;

[SwaggerSchema(Description = "Request body for creating or updating a zone.")]
public class UpsertZoneRequestDto
{
    [SwaggerSchema(Description = "The zone type. Supported values: subnet, static.")]
    public string Type { get; set; } = string.Empty;

    [SwaggerSchema(Description = "The display name of the zone.")]
    public string Name { get; set; } = string.Empty;

    [SwaggerSchema(Description = "Artifacts assigned to machines inside the zone.")]
    public List<string> AssignedArtifactIds { get; set; } = [];

    [SwaggerSchema(Description = "The subnet in CIDR notation for subnet zones.")]
    public string? Subnet { get; set; }

    [SwaggerSchema(Description = "The machine ids included in a static zone.")]
    public List<string>? MachineIds { get; set; }
}
