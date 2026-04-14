using boottorrent_lib.communication;
using boottorrent_lib.communication.message;

namespace btserver.handler;

public class MachineStoppedHandler(ILogger<MachineStartedHandler> logger) : IMessageHandler<MachineStoppedMessage>
{
    public string MessageType => MachineStoppedMessage.MessageType;

    public Task HandleAsync(MqttTopicContext context, MachineStoppedMessage message)
    {
        logger.LogInformation("Machine stopped: {ClientIdentifier} IP: {IPAddress}", context.TargetId, message.IPAddress);
        return Task.CompletedTask;
    }
}