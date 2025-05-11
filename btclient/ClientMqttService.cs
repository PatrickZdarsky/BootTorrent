using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using Microsoft.Extensions.Options;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace btclient;

public class ClientMqttService : MqttMessageService
{
    public readonly BTClientSettings ClientSettings;
    
    public event EventHandler? MqttConnectionEstablished;
    
    public ClientMqttService(IOptions<MqttSettings> mqttOptions, ILogger<ClientMqttService> logger, 
        MessageDispatcher dispatcher, IOptions<BTClientSettings> btClientOptions, 
        IHostApplicationLifetime applicationLifetime)
        : base(MqttOptionsFactory.Create(mqttOptions.Value), logger, dispatcher)
    {
        ClientSettings = btClientOptions.Value;
        
        applicationLifetime.ApplicationStopping.Register(async () =>
        {
            await PublishAsync(new MachineStoppedMessage() { IPAddress = "TEST" },
                EventFromMachine(MachineStoppedMessage.MessageType));
        });
    }

    protected override async Task HandleConnectedAsync(MqttClientConnectedEventArgs eventArgs)
    {
        MqttClient.ApplicationMessageReceivedAsync += HandleMessageReceivedAsync;
        await MqttClient.SubscribeAsync("boottorrent/cmd/global/#", MqttQualityOfServiceLevel.AtLeastOnce);
        await MqttClient.SubscribeAsync($"boottorrent/cmd/client/{ClientSettings.ClientIdentifier}/#", MqttQualityOfServiceLevel.AtLeastOnce);
        // await MqttClient.SubscribeAsync($"boottorrent/cmd/zone/{ZONE}", MqttQualityOfServiceLevel.AtLeastOnce);
        
        // Fire the event after the connection is fully set up
        //Todo: We might wanna check if this is a reconnect or not
        MqttConnectionEstablished?.Invoke(this, EventArgs.Empty);
        
        
    }

    protected override Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs eventArgs)
    {
        MqttClient.ApplicationMessageReceivedAsync -= HandleMessageReceivedAsync;
        return Task.CompletedTask;
    }

    public MqttTopicContext EventFromMachine(string messageType)
    {
        return MqttTopicContext.CreateEventFromMachine(ClientSettings.ClientIdentifier, messageType);
    }
}