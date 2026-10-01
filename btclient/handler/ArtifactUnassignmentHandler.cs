using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using btclient.artifact;

namespace btclient.handler;

public class ArtifactUnassignmentHandler(ILogger<ArtifactUnassignmentHandler> logger, ArtifactRegistry artifactRegistry) 
    : IMessageHandler<ArtifactUnassignmentMessage>
{
    public string MessageType => ArtifactUnassignmentMessage.MessageType;

    /// <summary>Removes the assigned artifact and its local torrent data when an unassignment arrives.</summary>
    /// <param name="context">The MQTT topic context.</param>
    /// <param name="message">The artifact unassignment command.</param>
    /// <returns>A task for local artifact removal.</returns>
    public async Task HandleAsync(MqttTopicContext context, ArtifactUnassignmentMessage message)
    {
        logger.LogDebug("Received torrent unassignment for artifact {ArtifactId}", message.ArtifactId);

        await artifactRegistry.RemoveArtifactAsync(message.ArtifactId);
    }
}
