using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using btclient.artifact;

namespace btclient.handler;

public class ArtifactAssignmentHandler(ILogger<ArtifactAssignmentHandler> logger, ArtifactRegistry artifactRegistry)
    : IMessageHandler<ArtifactAssignmentMessage>
{
    public string MessageType => ArtifactAssignmentMessage.MessageType;

    public async Task HandleAsync(MqttTopicContext context, ArtifactAssignmentMessage message)
    {
        logger.LogDebug("Received torrent assignment for job {JobId}", message.TorrentJob.JobId);
        
        await artifactRegistry.AddJob(message.TorrentJob);
    }
}