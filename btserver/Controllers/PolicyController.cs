using btserver.Controllers.Dto;
using btserver.Swarm;
using btserver.torrent;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace btserver.Controllers;

[ApiController]
[Route("api/v1/policies")]
[SwaggerTag("Torrent access and proxy election policies.")]
public class PolicyController(
    ITorrentAccessPolicyRegistry torrentAccessPolicyRegistry,
    ProxyElectionService election) : ControllerBase
{
    /// <summary>Lists registered torrent access policies.</summary>
    /// <returns>The policy names and priorities.</returns>
    [HttpGet]
    public ActionResult<List<PolicyDto>> GetPolicies() => Ok(torrentAccessPolicyRegistry.GetPolicies()
        .Select(policy => new PolicyDto { Name = policy.Name, Priority = policy.Priority }).ToList());

    /// <summary>Gets persisted or default proxy settings for a distribution zone.</summary>
    /// <param name="zoneId">The distribution zone ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The settings, or 404 for an unknown distribution zone.</returns>
    [HttpGet("zones/{zoneId:guid}/proxies")]
    public async Task<ActionResult<ProxyZoneConfigurationDto>> GetProxyConfiguration(Guid zoneId, CancellationToken cancellationToken)
    {
        var configuration = await election.GetConfigurationAsync(zoneId, cancellationToken);
        return configuration is null ? NotFound() : Ok(new ProxyZoneConfigurationDto
        {
            ZoneId = zoneId,
            DesiredInitialCount = configuration.DesiredInitialCount,
            SelectionPolicyKey = configuration.SelectionPolicyKey,
            PreferredMachineIds = configuration.PreferredMachineIds
        });
    }

    /// <summary>Updates a distribution zone's proxy count and selection preferences.</summary>
    /// <param name="zoneId">The distribution zone ID.</param>
    /// <param name="request">The desired count, policy key, and preferred machine IDs.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The saved settings, 400 for invalid settings, or 404 for an unknown zone.</returns>
    [HttpPut("zones/{zoneId:guid}/proxies")]
    public async Task<ActionResult<ProxyZoneConfigurationDto>> UpdateProxyConfiguration(
        Guid zoneId, [FromBody] UpdateProxyZoneConfigurationRequestDto request, CancellationToken cancellationToken)
    {
        if (request.DesiredInitialCount < 1 || request.SelectionPolicyKey != "stable-id")
            return BadRequest("DesiredInitialCount must be positive and SelectionPolicyKey must be 'stable-id'.");
        var updated = await election.UpdateConfigurationAsync(zoneId, request.DesiredInitialCount,
            request.SelectionPolicyKey, request.PreferredMachineIds, cancellationToken);
        if (!updated) return NotFound();
        var configuration = (await election.GetConfigurationAsync(zoneId, cancellationToken))!;
        return Ok(new ProxyZoneConfigurationDto
        {
            ZoneId = zoneId,
            DesiredInitialCount = configuration.DesiredInitialCount,
            SelectionPolicyKey = configuration.SelectionPolicyKey,
            PreferredMachineIds = configuration.PreferredMachineIds
        });
    }

    /// <summary>Reconciles and retrieves the current proxy designations for an assigned artifact.</summary>
    /// <param name="zoneId">The distribution zone ID.</param>
    /// <param name="artifactId">The artifact ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Proxy status, or 404 if the artifact is not assigned within the zone.</returns>
    [HttpGet("zones/{zoneId:guid}/artifacts/{artifactId}/proxies")]
    public async Task<ActionResult<ProxyStatusDto>> GetProxyStatus(Guid zoneId, string artifactId, CancellationToken cancellationToken)
    {
        if (!await election.IsArtifactAssignedAsync(zoneId, artifactId, cancellationToken)) return NotFound();
        await election.ReconcileAsync(zoneId, artifactId, cancellationToken);
        var configuration = await election.GetConfigurationAsync(zoneId, cancellationToken);
        var designations = await election.GetDesignationsAsync(zoneId, artifactId, cancellationToken);
        return Ok(new ProxyStatusDto
        {
            ZoneId = zoneId,
            ArtifactId = artifactId,
            DesiredInitialCount = configuration?.DesiredInitialCount ?? 1,
            Designations = designations.Select(item => new ProxyDesignationDto
            {
                MachineId = item.MachineId, SelectedAtUtc = item.SelectedAtUtc, Reason = item.Reason
            }).ToList()
        });
    }

    /// <summary>Sets the proxy designations for an artifact to the supplied eligible machines.</summary>
    /// <param name="zoneId">The distribution zone ID.</param>
    /// <param name="artifactId">The artifact ID.</param>
    /// <param name="request">The distinct machine IDs to designate.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Current status, or 400 when the selection is invalid.</returns>
    [HttpPut("zones/{zoneId:guid}/artifacts/{artifactId}/proxies")]
    public async Task<ActionResult<ProxyStatusDto>> SetProxyDesignations(
        Guid zoneId, string artifactId, [FromBody] SetProxyDesignationsRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            await election.SetDesignationsAsync(zoneId, artifactId, request.MachineIds, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        return await GetProxyStatus(zoneId, artifactId, cancellationToken);
    }

    /// <summary>Gets the latest 100 proxy designation and revocation events for an artifact.</summary>
    /// <param name="zoneId">The distribution zone ID.</param>
    /// <param name="artifactId">The artifact ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Transitions from newest to oldest.</returns>
    [HttpGet("zones/{zoneId:guid}/artifacts/{artifactId}/proxy-transitions")]
    public async Task<ActionResult<List<ProxyTransitionDto>>> GetProxyTransitions(Guid zoneId, string artifactId, CancellationToken cancellationToken)
    {
        var transitions = await election.GetTransitionsAsync(zoneId, artifactId, cancellationToken);
        return Ok(transitions.Select(item => new ProxyTransitionDto
        {
            MachineId = item.MachineId, Action = item.Action, Reason = item.Reason,
            OccurredAtUtc = item.OccurredAtUtc, Generation = item.Generation
        }).ToList());
    }
}
