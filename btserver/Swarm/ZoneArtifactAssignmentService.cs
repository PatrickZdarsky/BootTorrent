using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using boottorrent_lib.torrent;
using boottorrent_lib.client;
using btserver.torrent;
using btserver.Zone;
using btserver.Data;
using Microsoft.EntityFrameworkCore;

namespace btserver.Swarm;

/// <summary>Publishes artifact assignment commands to zone or machine MQTT topics.</summary>
public class ZoneArtifactAssignmentService(
    ITorrentArtifactRegistry artifactRegistry,
    Lazy<ServerMqttService> mqttService,
    BtDbContext dbContext,
    IActiveMachineSource machineSource) : IArtifactAssignmentPublisher
{
    /// <summary>Publishes an artifact assignment to a zone.</summary>
    /// <param name="zone">The destination zone.</param>
    /// <param name="artifactId">The registered artifact ID.</param>
    /// <param name="cancellationToken">Cancels publication.</param>
    /// <returns>A task for the publication.</returns>
    public async Task PublishAssignmentAsync(Zone.Zone zone, string artifactId, CancellationToken cancellationToken)
        => await PublishAssignmentAsync(zone.Id, artifactId, cancellationToken);

    /// <summary>Publishes an artifact assignment to a zone ID, including the default distribution zone.</summary>
    /// <param name="zoneId">The destination zone ID.</param>
    /// <param name="artifactId">The registered artifact ID.</param>
    /// <param name="cancellationToken">Cancels publication.</param>
    /// <returns>A task for the publication.</returns>
    public async Task PublishAssignmentAsync(Guid zoneId, string artifactId, CancellationToken cancellationToken)
    {
        var artifact = await artifactRegistry.GetArtifactByIdAsync(artifactId);
        var job = CreateTorrentJob(artifact);

        cancellationToken.ThrowIfCancellationRequested();
        await mqttService.Value.PublishAsync(
            new ArtifactAssignmentMessage
            {
                TorrentJob = job
            },
            MqttTopicContext.CreateCommandForZone(zoneId.ToString(), ArtifactAssignmentMessage.MessageType));
    }

    /// <summary>Publishes an assignment directly to a machine, typically to replay work after it acknowledges its configuration.</summary>
    /// <param name="machineId">The destination machine ID.</param>
    /// <param name="artifactId">The registered artifact ID.</param>
    /// <param name="cancellationToken">Cancels publication.</param>
    /// <returns>A task for the publication.</returns>
    public async Task PublishAssignmentToMachineAsync(string machineId, string artifactId, CancellationToken cancellationToken)
    {
        var artifact = await artifactRegistry.GetArtifactByIdAsync(artifactId);
        cancellationToken.ThrowIfCancellationRequested();
        await mqttService.Value.PublishAsync(
            new ArtifactAssignmentMessage { TorrentJob = CreateTorrentJob(artifact) },
            MqttTopicContext.CreateCommandForMachine(machineId, ArtifactAssignmentMessage.MessageType));
    }

    /// <summary>Unassigns an artifact from affected zone members without another applicable assignment.</summary>
    /// <param name="zone">The destination zone.</param>
    /// <param name="artifactId">The artifact ID.</param>
    /// <param name="cancellationToken">Cancels publication.</param>
    /// <returns>A task for the publication.</returns>
    public async Task PublishUnassignmentAsync(Zone.Zone zone, string artifactId, CancellationToken cancellationToken)
    {
        await PublishUnassignmentAsync(zone.Id, artifactId, cancellationToken);
    }

    /// <summary>Unassigns an artifact from affected members of a zone ID, including the default distribution zone.</summary>
    /// <param name="zoneId">The destination zone ID.</param>
    /// <param name="artifactId">The artifact ID.</param>
    /// <param name="cancellationToken">Cancels publication.</param>
    /// <returns>A task for the publication.</returns>
    public async Task PublishUnassignmentAsync(Guid zoneId, string artifactId, CancellationToken cancellationToken)
        => await PublishUnassignmentAsync([zoneId], artifactId, cancellationToken);

    /// <summary>Unassigns an artifact only from active machines affected by these zones that have no remaining assignment.</summary>
    /// <param name="zoneIds">Zones whose artifact assignment was removed.</param>
    /// <param name="artifactId">The artifact ID.</param>
    /// <param name="cancellationToken">Cancels database lookup and publication.</param>
    /// <returns>A task for all machine-scoped publications.</returns>
    public async Task PublishUnassignmentAsync(IEnumerable<Guid> zoneIds, string artifactId, CancellationToken cancellationToken)
    {
        var affectedZoneIds = zoneIds.ToHashSet();
        if (affectedZoneIds.Count == 0) return;
        var zones = await dbContext.Zones.AsNoTracking().ToListAsync(cancellationToken);
        var defaultAssigned = await dbContext.DefaultZoneArtifactAssignments.AsNoTracking()
            .AnyAsync(item => item.ArtifactId == artifactId, cancellationToken);
        var recipients = SelectUnassignmentRecipients(
            affectedZoneIds, artifactId, zones, defaultAssigned, machineSource.GetActiveMachines());

        foreach (var machineId in recipients)
            await PublishUnassignmentToMachineAsync(machineId, artifactId, cancellationToken);
    }

    /// <summary>Selects affected machines that no longer receive an artifact from any matching zone.</summary>
    public static IReadOnlyList<string> SelectUnassignmentRecipients(
        IReadOnlySet<Guid> affectedZoneIds, string artifactId, IReadOnlyCollection<Zone.Zone> zones,
        bool defaultAssigned, IReadOnlyCollection<Machine> activeMachines)
    {
        return activeMachines.Where(machine =>
        {
            var inDefault = ZoneManager.Resolve(machine, zones).DistributionZoneId ==
                ZoneManager.DefaultDistributionZoneId.ToString();
            var affected = zones.Any(zone => affectedZoneIds.Contains(zone.Id) && zone.Contains(machine)) ||
                (affectedZoneIds.Contains(ZoneManager.DefaultDistributionZoneId) && inDefault);
            var stillAssigned = zones.Any(zone => zone.Contains(machine) &&
                zone.AssignedArtifactIds.Contains(artifactId, StringComparer.Ordinal)) ||
                (inDefault && defaultAssigned);
            return affected && !stillAssigned;
        }).Select(machine => machine.Id).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc />
    public async Task PublishUnassignmentToMachineAsync(string machineId, string artifactId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await mqttService.Value.PublishAsync(
            new ArtifactUnassignmentMessage
            {
                ArtifactId = artifactId
            },
            MqttTopicContext.CreateCommandForMachine(machineId, ArtifactUnassignmentMessage.MessageType));
    }

    /// <summary>Creates the client download job carried by an assignment command.</summary>
    /// <param name="artifact">The registered torrent artifact.</param>
    /// <returns>A job containing the artifact identity and torrent URL.</returns>
    public static TorrentJob CreateTorrentJob(TorrentArtifact artifact)
    {
        return new TorrentJob
        {
            Name = artifact.Name,
            ArtifactId = artifact.ID,
            TorrentFileUrl = artifact.TorrentFileUrl,
            FileSha256 = artifact.IntegritySpec.FileSha256,
            DestinationSelector = null,
            SavePath = null
        };
    }
}
