using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using Microsoft.Extensions.Options;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace btserver;

public class ServerMqttService : MqttMessageService
{
    public ServerMqttService(IOptions<MqttSettings> mqttOptions, ILogger<ServerMqttService> logger, MessageDispatcher dispatcher) 
        : base(MqttOptionsFactory.Create(mqttOptions.Value), logger, dispatcher)
    {
    }

    protected override async Task HandleConnectedAsync(MqttClientConnectedEventArgs eventArgs)
    {
        MqttClient.ApplicationMessageReceivedAsync += HandleMessageReceivedAsync;
        await MqttClient.SubscribeAsync("boottorrent/evt/#", MqttQualityOfServiceLevel.AtLeastOnce);

        await PublishAsync(new MachineStartedMessage { IPAddress = "TEST" },
            MqttTopicContext.CreateEventFromMachine("Local", MachineStartedMessage.MessageType));
    }

    protected override Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs eventArgs)
    {
        MqttClient.ApplicationMessageReceivedAsync -= HandleMessageReceivedAsync;
        return Task.CompletedTask;
    }
}