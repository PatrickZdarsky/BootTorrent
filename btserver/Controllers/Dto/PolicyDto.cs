using Swashbuckle.AspNetCore.Annotations;

namespace btserver.Controllers.Dto;

[SwaggerSchema(Description = "Represents an available torrent access policy.")]
public class PolicyDto
{
    [SwaggerSchema(Description = "The unique policy name.")]
    public string Name { get; set; } = string.Empty;

    [SwaggerSchema(Description = "The policy priority. Higher priority policies are evaluated first.")]
    public int Priority { get; set; }

}
