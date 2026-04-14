using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using boottorrent_lib.torrent;
using btserver.torrent;

namespace btserver.handler;

public class MachineStartedHandler(ILogger<MachineStartedHandler> logger, ITorrentArtifactRegistry registry, Lazy<ServerMqttService> mqttService) : IMessageHandler<MachineStartedMessage>
{
    public string MessageType => MachineStartedMessage.MessageType;

    public async Task HandleAsync(MqttTopicContext context, MachineStartedMessage message)
    {
        logger.LogInformation("Machine started: {ClientIdentifier} IP: {IPAddress}", context.TargetId, message.IPAddress);

        var job = new TorrentJob
        {
            Artifact = (await registry.GetRegisteredArtifacts()).Values.First(),
            DestinationSelector = null,
            SavePath = null
        };
        await mqttService.Value.PublishAsync(new TorrentAssignmentMessage()
        {
            TorrentJob = job
        }, MqttTopicContext.CreateCommandForMachine(context.TargetId, TorrentAssignmentMessage.MessageType));
    }
}