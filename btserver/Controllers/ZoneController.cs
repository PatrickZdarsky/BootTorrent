using btserver.Controllers.Dto;
using btserver.Controllers.Mapping;
using btserver.Data;
using btserver.Zone;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace btserver.Controllers;

[ApiController]
[Route("api/v1/zones")]
[SwaggerTag("Provides operations for managing zones.")]
public class ZoneController(BtDbContext dbContext) : ControllerBase
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

        return Ok(zones.Select(zone => zone.ToDto()).ToList());
    }

    [HttpGet("{id:guid}")]
    [SwaggerOperation(Summary = "Retrieves a single zone.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The requested zone.", typeof(ZoneDto))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "The zone was not found.")]
    public async Task<ActionResult<ZoneDto>> GetZone(Guid id, CancellationToken cancellationToken)
    {
        var zone = await dbContext.Zones
            .AsNoTracking()
            .FirstOrDefaultAsync(existingZone => existingZone.Id == id, cancellationToken);

        return zone is null ? NotFound() : Ok(zone.ToDto());
    }

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

        dbContext.Zones.Add(zone);
        await dbContext.SaveChangesAsync(cancellationToken);

        var dto = zone.ToDto();
        return CreatedAtAction(nameof(GetZone), new { id = zone.Id }, dto);
    }

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

        if (!TryCreateZone(id, request, out var updatedZone, out var validationError))
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
        existingZone.AssignedArtifactIds = updatedZone.AssignedArtifactIds;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(existingZone.ToDto());
    }

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

        var subnetZonePolicyConfiguration = await dbContext.SubnetZonePolicyConfigurations
            .FirstOrDefaultAsync(configuration => configuration.ZoneId == id, cancellationToken);

        if (subnetZonePolicyConfiguration is not null)
        {
            dbContext.SubnetZonePolicyConfigurations.Remove(subnetZonePolicyConfiguration);
        }

        dbContext.Zones.Remove(zone);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private bool TryCreateZone(Guid id, UpsertZoneRequestDto request, out Zone.Zone zone, out string validationError)
    {
        validationError = string.Empty;
        zone = null!;

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            validationError = "Zone name is required.";
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
