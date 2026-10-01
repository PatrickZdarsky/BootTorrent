using btserver.Controllers.Dto;
using btserver.Controllers.Mapping;
using btserver.Config;
using btserver.Data;
using btserver.Swarm;
using btserver.torrent;
using btserver.Zone;
using btserver.torrent.monotorrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace btserver.Controllers;

[ApiController]
[Route("api/v1/artifacts")]
[SwaggerTag("Provides operations for managing artifacts and assigning them to zones.")]
public class ArtifactController(
    ILogger<ArtifactController> logger,
    ITorrentArtifactRegistry artifactRegistry,
    BtDbContext dbContext,
    ZoneArtifactAssignmentService zoneArtifactAssignmentService,
    ProxyElectionService proxyElectionService,
    MonoTorrentSeederService seeder,
    IOptions<TorrentConfig> torrentOptions) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(Summary = "Retrieves all registered artifacts.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The registered artifacts.", typeof(List<ArtifactDto>))]
    public async Task<ActionResult<List<ArtifactDto>>> GetArtifacts(CancellationToken cancellationToken)
    {
        var artifacts = (await artifactRegistry.GetRegisteredArtifacts()).Values
            .OrderBy(artifact => artifact.Name, StringComparer.Ordinal)
            .ToList();

        var zones = await dbContext.Zones
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var defaultAssignments = (await dbContext.DefaultZoneArtifactAssignments.AsNoTracking()
            .Select(assignment => assignment.ArtifactId).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        return Ok(artifacts
            .Select(artifact => artifact.ToDto(GetAssignedZoneIds(artifact.ID, zones, defaultAssignments.Contains(artifact.ID))))
            .ToList());
    }

    [HttpGet("{artifactId}")]
    [SwaggerOperation(Summary = "Retrieves a single artifact.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The requested artifact.", typeof(ArtifactDto))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "The artifact was not found.")]
    public async Task<ActionResult<ArtifactDto>> GetArtifact(string artifactId, CancellationToken cancellationToken)
    {
        var artifact = await TryGetArtifactAsync(artifactId);
        if (artifact is null)
        {
            return NotFound();
        }

        var zones = await dbContext.Zones
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var defaultAssigned = await dbContext.DefaultZoneArtifactAssignments
            .AnyAsync(assignment => assignment.ArtifactId == artifact.ID, cancellationToken);

        return Ok(artifact.ToDto(GetAssignedZoneIds(artifact.ID, zones, defaultAssigned)));
    }

    [HttpPost]
    [SwaggerOperation(Summary = "Creates and registers a new artifact.")]
    [SwaggerResponse(StatusCodes.Status201Created, "The artifact was created.", typeof(ArtifactDto))]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "The artifact request was invalid.")]
    public async Task<ActionResult<ArtifactDto>> CreateArtifact([FromBody] CreateArtifactRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ValidationProblem(nameof(CreateArtifactRequestDto.Name), "Artifact name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            return ValidationProblem(nameof(CreateArtifactRequestDto.FilePath), "Artifact file path is required.");
        }

        if (!TryResolveAllowedArtifactPath(request.FilePath, out var resolvedFilePath, out var validationError))
        {
            return ValidationProblem(nameof(CreateArtifactRequestDto.FilePath), validationError);
        }

        var artifact = await artifactRegistry.CreateAndRegisterTorrentAsync(
            request.Name.Trim(),
            request.Description.Trim(),
            resolvedFilePath,
            cancellationToken);

        await seeder.EnsureSeedingAsync(artifact.ID, cancellationToken);

        var dto = artifact.ToDto([]);
        return CreatedAtAction(nameof(GetArtifact), new { artifactId = artifact.ID }, dto);
    }

    [HttpDelete("{artifactId}")]
    [SwaggerOperation(Summary = "Unregisters an artifact and removes all zone assignments.")]
    [SwaggerResponse(StatusCodes.Status204NoContent, "The artifact was deleted.")]
    [SwaggerResponse(StatusCodes.Status404NotFound, "The artifact was not found.")]
    public async Task<IActionResult> DeleteArtifact(string artifactId, CancellationToken cancellationToken)
    {
        var artifact = await TryGetArtifactAsync(artifactId);
        if (artifact is null)
        {
            return NotFound();
        }

        var zones = (await dbContext.Zones.ToListAsync(cancellationToken))
            .Where(zone => zone.AssignedArtifactIds.Contains(artifactId, StringComparer.Ordinal)).ToList();
        var defaultAssignment = await dbContext.DefaultZoneArtifactAssignments
            .FirstOrDefaultAsync(item => item.ArtifactId == artifactId, cancellationToken);

        var affectedZones = zones
            .Select(zone => (zone.Id, zone.Name))
            .ToList();

        foreach (var zone in zones)
        {
            zone.AssignedArtifactIds = zone.AssignedArtifactIds
                .Where(existingArtifactId => !string.Equals(existingArtifactId, artifactId, StringComparison.Ordinal))
                .ToList();
        }

        if (defaultAssignment is not null)
        {
            dbContext.DefaultZoneArtifactAssignments.Remove(defaultAssignment);
            affectedZones.Add((ZoneManager.DefaultDistributionZoneId, "Default distribution zone"));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await proxyElectionService.ReconcileAllAsync(cancellationToken);

        try
        {
            await zoneArtifactAssignmentService.PublishUnassignmentAsync(
                affectedZones.Select(zone => zone.Id), artifactId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Artifact {ArtifactId} was unassigned in the database, but publishing machine unassignment commands failed.", artifactId);
            return Problem(
                title: "Artifact unassignment publish failed",
                detail: "Artifact assignments were removed from server state, but publishing machine unassignment commands failed.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var artifactUnregistered = await artifactRegistry.UnregisterArtifactAsync(artifactId, deleteFiles: true, cancellationToken);
        if (!artifactUnregistered)
        {
            logger.LogWarning(
                "Artifact {ArtifactId} was already removed from the registry before deletion completed. Returning success because the desired end state has been reached.",
                artifactId);
        }

        return NoContent();
    }

    /// <summary>Persists an artifact assignment, reconciles proxies, and publishes the zone command.</summary>
    /// <param name="artifactId">The registered artifact ID.</param>
    /// <param name="zoneId">The target zone ID, including the default distribution zone.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The zone's assignments, or 404 when the artifact or zone does not exist.</returns>
    [HttpPut("{artifactId}/zones/{zoneId:guid}")]
    [SwaggerOperation(Summary = "Assigns an artifact to a zone and publishes a zone-scoped assignment command.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The zone assignment was updated.", typeof(ZoneArtifactAssignmentResultDto))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "The artifact or zone was not found.")]
    public async Task<ActionResult<ZoneArtifactAssignmentResultDto>> AssignArtifactToZone(
        string artifactId,
        Guid zoneId,
        CancellationToken cancellationToken)
    {
        var artifact = await TryGetArtifactAsync(artifactId);
        if (artifact is null)
        {
            return NotFound();
        }

        if (zoneId == ZoneManager.DefaultDistributionZoneId)
        {
            if (!await dbContext.DefaultZoneArtifactAssignments.AnyAsync(item => item.ArtifactId == artifactId, cancellationToken))
            {
                dbContext.DefaultZoneArtifactAssignments.Add(new DefaultZoneArtifactAssignment { ArtifactId = artifactId });
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            await proxyElectionService.ReconcileAllAsync(cancellationToken);
            await zoneArtifactAssignmentService.PublishAssignmentAsync(zoneId, artifactId, cancellationToken);
            return Ok(new ZoneArtifactAssignmentResultDto { ZoneId = zoneId, AssignedArtifactIds = [artifactId] });
        }

        var zone = await dbContext.Zones
            .FirstOrDefaultAsync(existingZone => existingZone.Id == zoneId, cancellationToken);

        if (zone is null)
        {
            return NotFound();
        }

        if (!zone.AssignedArtifactIds.Contains(artifactId, StringComparer.Ordinal))
        {
            zone.AssignedArtifactIds = zone.AssignedArtifactIds
                .Append(artifactId)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await proxyElectionService.ReconcileAllAsync(cancellationToken);
        await zoneArtifactAssignmentService.PublishAssignmentAsync(zone, artifactId, cancellationToken);

        return Ok(new ZoneArtifactAssignmentResultDto
        {
            ZoneId = zone.Id,
            AssignedArtifactIds = zone.AssignedArtifactIds
        });
    }

    /// <summary>Removes an artifact assignment, reconciles proxies, and publishes the zone command.</summary>
    /// <param name="artifactId">The artifact ID.</param>
    /// <param name="zoneId">The target zone ID, including the default distribution zone.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The remaining assignments, or 404 when the zone does not exist.</returns>
    [HttpDelete("{artifactId}/zones/{zoneId:guid}")]
    [SwaggerOperation(Summary = "Unassigns an artifact from a zone and notifies machines without another assignment.")]
    [SwaggerResponse(StatusCodes.Status200OK, "The zone assignment was updated.", typeof(ZoneArtifactAssignmentResultDto))]
    [SwaggerResponse(StatusCodes.Status404NotFound, "The zone was not found.")]
    public async Task<ActionResult<ZoneArtifactAssignmentResultDto>> UnassignArtifactFromZone(
        string artifactId,
        Guid zoneId,
        CancellationToken cancellationToken)
    {
        if (zoneId == ZoneManager.DefaultDistributionZoneId)
        {
            var assignment = await dbContext.DefaultZoneArtifactAssignments
                .FirstOrDefaultAsync(item => item.ArtifactId == artifactId, cancellationToken);
            if (assignment is not null)
            {
                dbContext.DefaultZoneArtifactAssignments.Remove(assignment);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            await proxyElectionService.ReconcileAllAsync(cancellationToken);
            await zoneArtifactAssignmentService.PublishUnassignmentAsync(zoneId, artifactId, cancellationToken);
            return Ok(new ZoneArtifactAssignmentResultDto { ZoneId = zoneId });
        }

        var zone = await dbContext.Zones
            .FirstOrDefaultAsync(existingZone => existingZone.Id == zoneId, cancellationToken);

        if (zone is null)
        {
            return NotFound();
        }

        var changed = zone.AssignedArtifactIds.RemoveAll(existingArtifactId => string.Equals(existingArtifactId, artifactId, StringComparison.Ordinal)) > 0;
        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await proxyElectionService.ReconcileAllAsync(cancellationToken);
        await zoneArtifactAssignmentService.PublishUnassignmentAsync(zone, artifactId, cancellationToken);

        return Ok(new ZoneArtifactAssignmentResultDto
        {
            ZoneId = zone.Id,
            AssignedArtifactIds = zone.AssignedArtifactIds
        });
    }

    private async Task<boottorrent_lib.torrent.TorrentArtifact?> TryGetArtifactAsync(string artifactId)
    {
        var artifacts = await artifactRegistry.GetRegisteredArtifacts();
        return artifacts.GetValueOrDefault(artifactId);
    }

    private static IEnumerable<Guid> GetAssignedZoneIds(string artifactId, IEnumerable<btserver.Zone.Zone> zones, bool defaultAssigned)
    {
        var zoneIds = zones
            .Where(zone => zone.AssignedArtifactIds.Contains(artifactId, StringComparer.Ordinal))
            .Select(zone => zone.Id);
        return defaultAssigned ? zoneIds.Append(ZoneManager.DefaultDistributionZoneId) : zoneIds;
    }

    private ActionResult ValidationProblem(string key, string detail)
    {
        ModelState.AddModelError(key, detail);
        return ValidationProblem(ModelState);
    }

    private bool TryResolveAllowedArtifactPath(string requestedFilePath, out string resolvedFilePath, out string validationError)
    {
        resolvedFilePath = string.Empty;
        validationError = string.Empty;

        var uploadRoot = torrentOptions.Value.ArtifactUploadRoot;
        if (string.IsNullOrWhiteSpace(uploadRoot))
        {
            validationError = "Artifact upload root is not configured on the server.";
            return false;
        }

        try
        {
            resolvedFilePath = Path.GetFullPath(requestedFilePath.Trim());
        }
        catch (Exception)
        {
            validationError = "Artifact file path is invalid.";
            return false;
        }

        string resolvedUploadRoot;
        try
        {
            resolvedUploadRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(uploadRoot));
        }
        catch (Exception)
        {
            validationError = "Artifact upload root is invalid on the server.";
            return false;
        }

        if (!Path.IsPathRooted(resolvedFilePath))
        {
            validationError = "Artifact file path must be absolute.";
            return false;
        }

        if (!resolvedFilePath.StartsWith(resolvedUploadRoot, GetPathComparison()))
        {
            validationError = $"Artifact file path must be inside the configured upload root '{resolvedUploadRoot}'.";
            return false;
        }

        if (!System.IO.File.Exists(resolvedFilePath))
        {
            validationError = "Artifact file path does not exist.";
            return false;
        }

        return true;
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }
}
