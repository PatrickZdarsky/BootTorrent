using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using boottorrent_lib.torrent;
using btserver.torrent;
using btserver.Zone;

namespace btserver.Swarm;

/// <summary>Publishes artifact assignment commands to zone or machine MQTT topics.</summary>
public class ZoneArtifactAssignmentService(
    ITorrentArtifactRegistry artifactRegistry,
    Lazy<ServerMqttService> mqttService) : IArtifactAssignmentPublisher
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

    /// <summary>Publishes an artifact unassignment to a zone.</summary>
    /// <param name="zone">The destination zone.</param>
    /// <param name="artifactId">The artifact ID.</param>
    /// <param name="cancellationToken">Cancels publication.</param>
    /// <returns>A task for the publication.</returns>
    public async Task PublishUnassignmentAsync(Zone.Zone zone, string artifactId, CancellationToken cancellationToken)
    {
        await PublishUnassignmentAsync(zone.Id, artifactId, cancellationToken);
    }

    /// <summary>Publishes an artifact unassignment to a zone ID, including the default distribution zone.</summary>
    /// <param name="zoneId">The destination zone ID.</param>
    /// <param name="artifactId">The artifact ID.</param>
    /// <param name="cancellationToken">Cancels publication.</param>
    /// <returns>A task for the publication.</returns>
    public async Task PublishUnassignmentAsync(Guid zoneId, string artifactId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await mqttService.Value.PublishAsync(
            new ArtifactUnassignmentMessage
            {
                ArtifactId = artifactId
            },
            MqttTopicContext.CreateCommandForZone(zoneId.ToString(), ArtifactUnassignmentMessage.MessageType));
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
            DestinationSelector = null,
            SavePath = null
        };
    }
}
