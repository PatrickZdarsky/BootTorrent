using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using btclient.artifact;

namespace btclient.handler;

public class ArtifactUnassignmentHandler(ILogger<ArtifactUnassignmentHandler> logger, ArtifactRegistry artifactRegistry) 
    : IMessageHandler<ArtifactUnassignmentMessage>
{
    public string MessageType => ArtifactUnassignmentMessage.MessageType;

    public Task HandleAsync(MqttTopicContext context, ArtifactUnassignmentMessage message)
    {
        logger.LogDebug("Received torrent unassignment for artifact {ArtifactId}", message.ArtifactId);

        artifactRegistry.RemoveArtifact(message.ArtifactId);
        
        return Task.CompletedTask;
    }
}