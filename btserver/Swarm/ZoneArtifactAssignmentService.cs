using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using boottorrent_lib.torrent;
using btserver.torrent;
using btserver.Zone;

namespace btserver.Swarm;

public class ZoneArtifactAssignmentService(
    ITorrentArtifactRegistry artifactRegistry,
    Lazy<ServerMqttService> mqttService)
{
    public async Task PublishAssignmentAsync(Zone.Zone zone, string artifactId, CancellationToken cancellationToken)
    {
        var artifact = await artifactRegistry.GetArtifactByIdAsync(artifactId);
        var job = CreateTorrentJob(artifact);

        cancellationToken.ThrowIfCancellationRequested();
        await mqttService.Value.PublishAsync(
            new ArtifactAssignmentMessage
            {
                TorrentJob = job
            },
            MqttTopicContext.CreateCommandForZone(zone.Id.ToString(), ArtifactAssignmentMessage.MessageType));
    }

    public async Task PublishUnassignmentAsync(Zone.Zone zone, string artifactId, CancellationToken cancellationToken)
    {
        await PublishUnassignmentAsync(zone.Id, artifactId, cancellationToken);
    }

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
