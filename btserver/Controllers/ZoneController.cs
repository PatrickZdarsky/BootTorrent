using btserver.Controllers.Dto;
using btserver.Controllers.Mapping;
using btserver.Data;
using btserver.Swarm;
using btserver.Zone;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace btserver.Controllers;

[ApiController]
[Route("api/v1/zones")]
[SwaggerTag("Provides operations for managing zones.")]
public class ZoneController(
    BtDbContext dbContext,
    MachineRegistry machineRegistry,
    MachineConfigurationService machineConfigurationService,
    ProxyElectionService proxyElectionService) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Summary = "Retrieves all zones.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The configured zones.", typeof(List<ZoneDto>))]
    public async Task<ActionResult<List<ZoneDto>>> GetZones(CancellationToken cancellationToken)
    {
        var zones = await dbContext.Zones
            .AsNoTracking()
            .OrderBy(zone => zone.Name)
            .ToListAsync(cancellationToken);

        var result = zones.Select(zone => zone.ToDto()).ToList();
        var defaultArtifactIds = await dbContext.DefaultZoneArtifactAssignments.AsNoTracking()
            .Select(assignment => assignment.ArtifactId).ToListAsync(cancellationToken);
        result.Add(DefaultDistributionZoneDto(defaultArtifactIds));
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [SwaggerOperation(Summary = "Retrieves a single zone.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The requested zone.", typeof(ZoneDto))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "The zone was not found.")]
    public async Task<ActionResult<ZoneDto>> GetZone(Guid id, CancellationToken cancellationToken)
    {
        if (id == ZoneManager.DefaultDistributionZoneId)
        {
            var defaultArtifactIds = await dbContext.DefaultZoneArtifactAssignments.AsNoTracking()
                .Select(assignment => assignment.ArtifactId).ToListAsync(cancellationToken);
            return Ok(DefaultDistributionZoneDto(defaultArtifactIds));
        }

        var zone = await dbContext.Zones
            .AsNoTracking()
            .FirstOrDefaultAsync(existingZone => existingZone.Id == id, cancellationToken);

        return zone is null ? NotFound() : Ok(zone.ToDto());
    }

    /// <summary>Creates a zone, updates active machine memberships, and reconciles proxy elections.</summary>
    /// <param name="request">The zone definition and distribution role.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The new zone, or a validation response for invalid or overlapping distribution zones.</returns>
    [HttpPost]
    [SwaggerOperation(Summary = "Creates a new zone.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The zone was created.", typeof(ZoneDto))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The zone request was invalid.")]
    public async Task<ActionResult<ZoneDto>> CreateZone([FromBody] UpsertZoneRequestDto request, CancellationToken cancellationToken)
    {
        if (!TryCreateZone(Guid.NewGuid(), request, out var zone, out var validationError))
        {
            return ValidationProblem(validationError);
        }

        validationError = await GetDistributionConflictAsync(zone, cancellationToken);
        if (validationError is not null)
        {
            return ValidationProblem(validationError);
        }

        dbContext.Zones.Add(zone);
        await dbContext.SaveChangesAsync(cancellationToken);
        await proxyElectionService.ReconcileAllAsync(cancellationToken);
        await machineConfigurationService.EnsureConfigurationsAsync(machineRegistry.Machines.Values, cancellationToken);

        var dto = zone.ToDto();
        return CreatedAtAction(nameof(GetZone), new { id = zone.Id }, dto);
    }

    /// <summary>Updates a zone and recomputes affected machine memberships and proxy elections.</summary>
    /// <param name="id">The zone ID.</param>
    /// <param name="request">The updated zone definition.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The updated zone, or an error for an invalid or missing zone.</returns>
    [HttpPut("{id:guid}")]
    [SwaggerOperation(Summary = "Updates an existing zone.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The zone was updated.", typeof(ZoneDto))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The zone request was invalid.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "The zone was not found.")]
    public async Task<ActionResult<ZoneDto>> UpdateZone(Guid id, [FromBody] UpsertZoneRequestDto request, CancellationToken cancellationToken)
    {
        var existingZone = await dbContext.Zones
            .FirstOrDefaultAsync(zone => zone.Id == id, cancellationToken);

        if (existingZone is null)
        {
            return NotFound();
        }

        if (!string.Equals(GetZoneType(existingZone), request.Type, StringComparison.OrdinalIgnoreCase))
        {
            return ValidationProblem("Changing a zone type is not supported.");
        }

        if (!TryCreateZone(id, request, out var updatedZone, out var validationError, existingZone.IsDistributionZone))
        {
            return ValidationProblem(validationError);
        }

        validationError = await GetDistributionConflictAsync(updatedZone, cancellationToken);
        if (validationError is not null)
        {
            return ValidationProblem(validationError);
        }

        switch (existingZone)
        {
            case SubnetZone existingSubnetZone when updatedZone is SubnetZone updatedSubnetZone:
                existingSubnetZone.Subnet = updatedSubnetZone.Subnet;
                break;
            case StaticZone existingStaticZone when updatedZone is StaticZone updatedStaticZone:
                existingStaticZone.MachineIds = updatedStaticZone.MachineIds;
                break;
        }

        existingZone.Name = updatedZone.Name;
        existingZone.IsDistributionZone = updatedZone.IsDistributionZone;
        existingZone.AssignedArtifactIds = updatedZone.AssignedArtifactIds;
        await dbContext.SaveChangesAsync(cancellationToken);
        await proxyElectionService.ReconcileAllAsync(cancellationToken);
        await machineConfigurationService.EnsureConfigurationsAsync(machineRegistry.Machines.Values, cancellationToken);

        return Ok(existingZone.ToDto());
    }

    /// <summary>Deletes a zone and its proxy configuration, then recomputes memberships and elections.</summary>
    /// <param name="id">The zone ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>204 on deletion, or 404 when the zone does not exist.</returns>
    [HttpDelete("{id:guid}")]
    [SwaggerOperation(Summary = "Deletes a zone.")]
    [SwaggerResponse(StatusCodes.Status204NoContent, "The zone was deleted.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "The zone was not found.")]
    public async Task<IActionResult> DeleteZone(Guid id, CancellationToken cancellationToken)
    {
        var zone = await dbContext.Zones
            .FirstOrDefaultAsync(existingZone => existingZone.Id == id, cancellationToken);

        if (zone is null)
        {
            return NotFound();
        }

        dbContext.Zones.Remove(zone);
        var proxyConfiguration = await dbContext.ProxyZoneConfigurations
            .FirstOrDefaultAsync(configuration => configuration.ZoneId == id, cancellationToken);
        if (proxyConfiguration is not null) dbContext.ProxyZoneConfigurations.Remove(proxyConfiguration);
        await dbContext.SaveChangesAsync(cancellationToken);
        await proxyElectionService.ReconcileAllAsync(cancellationToken);
        await machineConfigurationService.EnsureConfigurationsAsync(machineRegistry.Machines.Values, cancellationToken);
        return NoContent();
    }

    private bool TryCreateZone(Guid id, UpsertZoneRequestDto request, out Zone.Zone zone, out string validationError, bool currentDistributionRole = true)
    {
        validationError = string.Empty;
        zone = null!;

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            validationError = "Zone name is required.";
            return false;
        }

        if (id == ZoneManager.DefaultDistributionZoneId)
        {
            validationError = "The default distribution zone ID is reserved.";
            return false;
        }

        var assignedArtifactIds = request.AssignedArtifactIds
            .Where(artifactId => !string.IsNullOrWhiteSpace(artifactId))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (string.Equals(request.Type, "subnet", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.Subnet))
            {
                validationError = "Subnet is required for subnet zones.";
                return false;
            }

            var subnetZone = new SubnetZone
            {
                Id = id,
                Name = request.Name.Trim(),
                IsDistributionZone = request.IsDistributionZone ?? currentDistributionRole,
                AssignedArtifactIds = assignedArtifactIds,
                Subnet = request.Subnet.Trim()
            };

            if (!subnetZone.TryParseNetwork(out _))
            {
                validationError = "Subnet must be a valid CIDR network.";
                return false;
            }

            zone = subnetZone;
            return true;
        }

        if (string.Equals(request.Type, "static", StringComparison.OrdinalIgnoreCase))
        {
            zone = new StaticZone
            {
                Id = id,
                Name = request.Name.Trim(),
                IsDistributionZone = request.IsDistributionZone ?? currentDistributionRole,
                AssignedArtifactIds = assignedArtifactIds,
                MachineIds = (request.MachineIds ?? [])
                    .Where(machineId => !string.IsNullOrWhiteSpace(machineId))
                    .Distinct(StringComparer.Ordinal)
                    .ToList()
            };

            return true;
        }

        validationError = "Zone type must be either 'subnet' or 'static'.";
        return false;
    }

    private ActionResult ValidationProblem(string detail)
    {
        ModelState.AddModelError(nameof(UpsertZoneRequestDto), detail);
        return ValidationProblem(ModelState);
    }

    private async Task<string?> GetDistributionConflictAsync(Zone.Zone candidate, CancellationToken cancellationToken)
    {
        if (!candidate.IsDistributionZone)
        {
            return null;
        }

        var others = await dbContext.Zones.AsNoTracking()
            .Where(zone => zone.Id != candidate.Id && zone.IsDistributionZone)
            .ToListAsync(cancellationToken);

        foreach (var other in others)
        {
            if (candidate is StaticZone candidateStatic && other is StaticZone otherStatic &&
                candidateStatic.MachineIds.Intersect(otherStatic.MachineIds, StringComparer.Ordinal).Any())
            {
                return $"Distribution zone overlaps with '{other.Name}' on a machine ID.";
            }

            if (candidate is SubnetZone candidateSubnet && other is SubnetZone otherSubnet &&
                SubnetsOverlap(candidateSubnet, otherSubnet))
            {
                return $"Distribution subnet overlaps with '{other.Name}'.";
            }

            if (machineRegistry.Machines.Values.Any(machine => candidate.Contains(machine) && other.Contains(machine)))
            {
                return $"Distribution zone overlaps with '{other.Name}' for an active machine.";
            }
        }

        return null;
    }

    private static bool SubnetsOverlap(SubnetZone left, SubnetZone right)
    {
        var leftAddress = left.Subnet.Split('/')[0];
        var rightAddress = right.Subnet.Split('/')[0];
        return left.Contains(new boottorrent_lib.client.Machine("subnet-check", rightAddress)) ||
               right.Contains(new boottorrent_lib.client.Machine("subnet-check", leftAddress));
    }

    private static ZoneDto DefaultDistributionZoneDto(List<string> assignedArtifactIds) => new()
    {
        Id = ZoneManager.DefaultDistributionZoneId,
        Type = "default",
        Name = "Default distribution zone",
        IsDistributionZone = true,
        AssignedArtifactIds = assignedArtifactIds
    };

    private static string GetZoneType(Zone.Zone zone)
    {
        return zone switch
        {
            SubnetZone => "subnet",
            StaticZone => "static",
            _ => string.Empty
        };
    }
}
