using Swashbuckle.AspNetCore.Annotations;

namespace btserver.Controllers.Dto;

[SwaggerSchema(Description = "Request body for creating or updating subnet-zone policy configuration for a zone.")]
public class UpsertSubnetZonePolicyConfigurationRequestDto
{
    [SwaggerSchema(Description = "The number of proxies to keep stable for the zone.")]
    public int ProxyCount { get; set; } = 1;

    [SwaggerSchema(Description = "Optional initial or preferred proxy machine ids for the zone.")]
    public List<string>? ProxyMachineIds { get; set; }
}
