using boottorrent_lib.communication;
using boottorrent_lib.communication.message;

namespace btserver.transport;

public class MachineStartedHandler : IMessageHandler<MachineStartedMessage>
{
    private readonly ILogger<MachineStartedHandler> _logger;

    public MachineStartedHandler(ILogger<MachineStartedHandler> logger)
    {
        _logger = logger;
    }

    public string MessageType => MachineStartedMessage.MessageType;

    public Task HandleAsync(MqttTopicContext context, MachineStartedMessage message)
    {
        _logger.LogInformation("Machine started: {IPAddress}", message.IPAddress);
        return Task.CompletedTask;
    }
}