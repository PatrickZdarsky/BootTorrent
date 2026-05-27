using btserver.Controllers.Dto;
using btserver.torrent;
using btserver.torrent.impl;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace btserver.Controllers;

[ApiController]
[Route("api/v1/policies")]
[SwaggerTag("Provides operations for querying and managing torrent access policies.")]
public class PolicyController(
    ITorrentAccessPolicyRegistry torrentAccessPolicyRegistry,
    SubnetZoneTorrentAccessPolicy subnetZoneTorrentAccessPolicy) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Summary = "Retrieves all registered torrent access policies.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The registered policies.", typeof(List<PolicyDto>))]
    public async Task<ActionResult<List<PolicyDto>>> GetPolicies(CancellationToken cancellationToken)
    {
        var subnetConfiguration = await subnetZoneTorrentAccessPolicy.GetConfigurationAsync(cancellationToken);
        var policies = torrentAccessPolicyRegistry.GetPolicies()
            .Select(policy => ToDto(policy, subnetConfiguration))
            .ToList();

        return Ok(policies);
    }

    [HttpGet("subnet-zone")]
    [SwaggerOperation(Summary = "Retrieves subnet-zone policy configuration.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The subnet-zone configuration.", typeof(SubnetZonePolicyDto))]
    public async Task<ActionResult<SubnetZonePolicyDto>> GetSubnetZonePolicy(CancellationToken cancellationToken)
    {
        return Ok(await subnetZoneTorrentAccessPolicy.GetConfigurationAsync(cancellationToken));
    }

    [HttpPut("subnet-zone/zones/{zoneId:guid}")]
    [SwaggerOperation(Summary = "Creates or updates subnet-zone policy configuration for a zone.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The updated zone configuration.", typeof(SubnetZonePolicyZoneConfigurationDto))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "The subnet zone was not found.")]
    public async Task<ActionResult<SubnetZonePolicyZoneConfigurationDto>> UpsertSubnetZonePolicyZoneConfiguration(
        Guid zoneId,
        [FromBody] UpsertSubnetZonePolicyConfigurationRequestDto request,
        CancellationToken cancellationToken)
    {
        var configuration = await subnetZoneTorrentAccessPolicy.UpsertZoneConfigurationAsync(
            zoneId,
            request.ProxyCount,
            request.ProxyMachineIds,
            cancellationToken);

        return configuration is null ? NotFound() : Ok(configuration);
    }

    [HttpDelete("subnet-zone/zones/{zoneId:guid}")]
    [SwaggerOperation(Summary = "Deletes subnet-zone policy configuration for a zone.")]
    [SwaggerResponse(StatusCodes.Status204NoContent, "The configuration was deleted.")]
    public async Task<IActionResult> DeleteSubnetZonePolicyZoneConfiguration(Guid zoneId, CancellationToken cancellationToken)
    {
        await subnetZoneTorrentAccessPolicy.DeleteZoneConfigurationAsync(zoneId, cancellationToken);
        return NoContent();
    }

    private static PolicyDto ToDto(ITorrentAccessPolicy policy, SubnetZonePolicyDto subnetConfiguration)
    {
        return new PolicyDto
        {
            Name = policy.Name,
            Priority = policy.Priority,
            SubnetZoneConfiguration = string.Equals(policy.Name, subnetConfiguration.Name, StringComparison.Ordinal)
                ? subnetConfiguration
                : null
        };
    }
}
