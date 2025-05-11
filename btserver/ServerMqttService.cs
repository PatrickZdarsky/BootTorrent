using boottorrent_lib.communication;
using Microsoft.Extensions.Options;
using MQTTnet.Client;
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
    }

    protected override Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs eventArgs)
    {
        MqttClient.ApplicationMessageReceivedAsync -= HandleMessageReceivedAsync;
        return Task.CompletedTask;
    }
}