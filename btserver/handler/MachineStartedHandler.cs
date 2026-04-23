using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using boottorrent_lib.torrent;
using btserver.settings;
using btserver.torrent;
using Microsoft.Extensions.Options;

namespace btserver.handler;

public class MachineStartedHandler(ILogger<MachineStartedHandler> logger, ITorrentArtifactRegistry registry, Lazy<ServerMqttService> mqttService, IOptions<TorrentSettings> settings) : IMessageHandler<MachineStartedMessage>
{
    public string MessageType => MachineStartedMessage.MessageType;

    public async Task HandleAsync(MqttTopicContext context, MachineStartedMessage message)
    {
        logger.LogInformation("Machine started: {ClientIdentifier} IP: {IPAddress}", context.TargetId, message.IPAddress);

        var artifact = (await registry.GetRegisteredArtifacts()).Values.First();
        var job = new TorrentJob
        {
            Name = artifact.Name,
            ArtifactId = artifact.ID,
            TorrentFileUrl = settings.Value.TrackerUrl + settings.Value.TorrentFileGetSuffix + artifact.Torrent.InfoHashV1,
            DestinationSelector = null,
            SavePath = null
        };
        await mqttService.Value.PublishAsync(new ArtifactAssignmentMessage()
        {
            TorrentJob = job
        }, MqttTopicContext.CreateCommandForMachine(context.TargetId, ArtifactAssignmentMessage.MessageType));
    }
}