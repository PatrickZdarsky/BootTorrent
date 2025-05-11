using boottorrent_lib.communication;
using boottorrent_lib.communication.message;

namespace btserver.transport;

public class MachineStoppedHandler : IMessageHandler<MachineStoppedMessage>
{
    private readonly ILogger<MachineStartedHandler> _logger;

    public MachineStoppedHandler(ILogger<MachineStartedHandler> logger)
    {
        _logger = logger;
    }

    public string MessageType => MachineStoppedMessage.MessageType;

    public Task HandleAsync(MqttTopicContext context, MachineStoppedMessage message)
    {
        _logger.LogInformation("Machine stopped: {ClientIdentifier} IP: {IPAddress}", context.TargetId, message.IPAddress);
        return Task.CompletedTask;
    }
}