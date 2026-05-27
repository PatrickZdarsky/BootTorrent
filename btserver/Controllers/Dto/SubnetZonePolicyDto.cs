using Swashbuckle.AspNetCore.Annotations;

namespace btserver.Controllers.Dto;

[SwaggerSchema(Description = "Configuration for the subnet-zone torrent access policy.")]
public class SubnetZonePolicyDto
{
    [SwaggerSchema(Description = "The policy name.")]
    public string Name { get; set; } = string.Empty;

    [SwaggerSchema(Description = "The policy priority.")]
    public int Priority { get; set; }

    [SwaggerSchema(Description = "Per-zone policy configuration.")]
    public List<SubnetZonePolicyZoneConfigurationDto> Zones { get; set; } = [];
}
