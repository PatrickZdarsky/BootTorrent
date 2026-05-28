using boottorrent_lib.communication;
using boottorrent_lib.communication.message;

namespace btclient.handler;

public class MachineConfigurationHandler(
    ILogger<MachineConfigurationHandler> logger,
    ClientMachineConfigurationService machineConfigurationService)
    : IMessageHandler<MachineConfigurationMessage>
{
    public string MessageType => MachineConfigurationMessage.MessageType;

    public async Task HandleAsync(MqttTopicContext context, MachineConfigurationMessage message)
    {
        logger.LogInformation(
            "Received machine configuration update for machine topic {MachineId} with hash {ConfigHash}.",
            context.TargetId,
            message.Configuration.ConfigHash);

        await machineConfigurationService.ApplyConfigurationAsync(message.Configuration);
    }
}
