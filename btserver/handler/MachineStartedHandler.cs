using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using boottorrent_lib.torrent;
using btserver.Config;
using btserver.torrent;
using Microsoft.Extensions.Options;

namespace btserver.handler;

public class MachineStartedHandler(ILogger<MachineStartedHandler> logger, 
    ITorrentArtifactRegistry registry, Lazy<ServerMqttService> mqttService, IOptions<TorrentConfig> settings) : IMessageHandler<MachineStartedMessage>
{
    public string MessageType => MachineStartedMessage.MessageType;

    public async Task HandleAsync(MqttTopicContext context, MachineStartedMessage message)
    {
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