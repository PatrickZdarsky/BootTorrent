using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using btserver.Machine;

namespace btserver.handler;

public class MachineStoppedHandler(ILogger<MachineStartedHandler> logger, MachineRegistry machineRegistry) : IMessageHandler<MachineStoppedMessage>
{
    public string MessageType => MachineStoppedMessage.MessageType;

    public Task HandleAsync(MqttTopicContext context, MachineStoppedMessage message)
    {
        machineRegistry.MachineStopped(context.TargetId!);
        return Task.CompletedTask;
    }
}