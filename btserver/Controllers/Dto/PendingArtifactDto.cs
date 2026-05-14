using Swashbuckle.AspNetCore.Annotations;

[SwaggerSchema(Description = "Represents an artifact that is in the process of being downloaded.")]
public class PendingArtifactDto
{
    [SwaggerSchema(Description = "The name of the artifact.")]
    public string Name { get; set; }
    [SwaggerSchema(Description = "The download progress, represented as a percentage.")]
    public double Progress { get; set; }
}
