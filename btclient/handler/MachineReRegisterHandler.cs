using boottorrent_lib.communication;
using boottorrent_lib.communication.message;

namespace btclient.handler;

public class MachineReRegisterHandler(ILogger<MachineReRegisterHandler> logger, ClientMqttService clientMqttService) : IMessageHandler<MachineReRegisterMessage>
{
    public string MessageType => MachineReRegisterMessage.MessageType;
    
    public async Task HandleAsync(MqttTopicContext context, MachineReRegisterMessage message)
    {
        logger.LogInformation("Received machine re-registration request. Sending MachineStartedMessage...");
        var machineStartedMessage = new MachineStartedMessage
        {
            IPAddress = NetworkHelper.GetPrimaryIPv4()?.ToString() ?? "UNKNOWN"
        };
        await clientMqttService.PublishAsync(machineStartedMessage, clientMqttService.EventFromMachine(MachineStartedMessage.MessageType));
    }
}