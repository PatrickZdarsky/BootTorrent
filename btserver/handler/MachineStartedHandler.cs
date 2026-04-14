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

        await mqttService.Value.PublishAsync(new TorrentAssignmentMessage()
        {
            TorrentJob = new TorrentJob
            {
                Artifact = (await registry.GetRegisteredArtifacts()).Values.First(),
                DestinationSelector = null,
                SavePath = null
            }
        }, MqttTopicContext.CreateCommandForMachine(context.TargetId, TorrentAssignmentMessage.MessageType));
    }
}