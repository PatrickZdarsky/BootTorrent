using Swashbuckle.AspNetCore.Annotations;

namespace btserver.Controllers.Dto;

[SwaggerSchema(Description = "Subnet-zone policy configuration for a single subnet zone.")]
public class SubnetZonePolicyZoneConfigurationDto
{
    [SwaggerSchema(Description = "The subnet zone identifier.")]
    public Guid ZoneId { get; set; }

    [SwaggerSchema(Description = "The zone name.")]
    public string ZoneName { get; set; } = string.Empty;

    [SwaggerSchema(Description = "The number of proxies to keep stable for the zone.")]
    public int ProxyCount { get; set; }

    [SwaggerSchema(Description = "The machine ids currently preferred as stable proxies.")]
    public List<string> ProxyMachineIds { get; set; } = [];
}
