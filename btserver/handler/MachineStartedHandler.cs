using boottorrent_lib.communication;
using boottorrent_lib.communication.message;

namespace btserver.transport;

public class MachineStartedHandler(ILogger<MachineStartedHandler> logger) : IMessageHandler<MachineStartedMessage>
{
    public string MessageType => MachineStartedMessage.MessageType;

    public Task HandleAsync(MqttTopicContext context, MachineStartedMessage message)
    {
        logger.LogInformation("Machine started: {ClientIdentifier} IP: {IPAddress}", context.TargetId, message.IPAddress);
        return Task.CompletedTask;
    }
}