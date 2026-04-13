using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using btclient.torrent;

namespace btclient.handler;

public class TorrentAssignmentHandler : IMessageHandler<TorrentAssignmentMessage>
{
    public string MessageType => TorrentAssignmentMessage.MessageType;
    
    private readonly ITorrentClient _torrentClient;
    private readonly ILogger<TorrentAssignmentHandler> _logger;

    public TorrentAssignmentHandler(ITorrentClient torrentClient, ILogger<TorrentAssignmentHandler> logger)
    {
        _torrentClient = torrentClient;
        _logger = logger;
    }
    
    public async Task HandleAsync(MqttTopicContext context, TorrentAssignmentMessage message)
    {
        _logger.LogInformation("Received torrent assignment for job {JobId}", message.TorrentJob.JobId);
        
        // Save message.TorrentJob.Artifact.torrent.TorrentFileBytes to a temporary file
        // 
    }
}