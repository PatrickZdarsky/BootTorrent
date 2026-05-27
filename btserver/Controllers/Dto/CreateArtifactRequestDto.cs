using Swashbuckle.AspNetCore.Annotations;

namespace btserver.Controllers.Dto;

[SwaggerSchema(Description = "Request body for creating a new artifact.")]
public class CreateArtifactRequestDto
{
    [SwaggerSchema(Description = "The artifact display name.")]
    public string Name { get; set; } = string.Empty;

    [SwaggerSchema(Description = "Optional human-readable description.")]
    public string Description { get; set; } = string.Empty;

    [SwaggerSchema(Description = "Absolute file path to the source artifact payload on the server.")]
    public string FilePath { get; set; } = string.Empty;
}
