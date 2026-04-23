using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using btclient.artifact;
using btclient.torrent;

namespace btclient.handler;

public class TorrentAssignmentHandler : IMessageHandler<ArtifactAssignmentMessage>
{
    public string MessageType => ArtifactAssignmentMessage.MessageType;
    
    private readonly ArtifactRegistry _artifactRegistry;
    private readonly ILogger<TorrentAssignmentHandler> _logger;

    public TorrentAssignmentHandler(ILogger<TorrentAssignmentHandler> logger, ArtifactRegistry artifactRegistry)
    {
        _logger = logger;
        _artifactRegistry = artifactRegistry;
    }
    
    public async Task HandleAsync(MqttTopicContext context, ArtifactAssignmentMessage message)
    {
        _logger.LogInformation("Received torrent assignment for job {JobId}", message.TorrentJob.JobId);
        
        await _artifactRegistry.AddJob(message.TorrentJob);
    }
}