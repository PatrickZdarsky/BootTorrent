using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace btclient;

public class ClientMqttService : MqttMessageService
{
    public readonly BTClientSettings ClientSettings;
    private readonly ILogger<ClientMqttService> _logger;
    private int _connectionGeneration;

    public bool IsConnected => MqttClient.IsConnected;
    public int ConnectionGeneration => Volatile.Read(ref _connectionGeneration);
    
    public ClientMqttService(IOptions<MqttSettings> mqttOptions, ILogger<ClientMqttService> logger,
        MessageDispatcher dispatcher, IOptions<BTClientSettings> btClientOptions)
        : base(MqttOptionsFactory.Create(mqttOptions.Value), logger, dispatcher)
    {
        ClientSettings = btClientOptions.Value;
        _logger = logger;
    }

    protected override async Task HandleConnectedAsync(MqttClientConnectedEventArgs eventArgs)
    {
        MqttClient.ApplicationMessageReceivedAsync += HandleMessageReceivedAsync;
        await MqttClient.SubscribeAsync("boottorrent/cmd/global/#", MqttQualityOfServiceLevel.AtLeastOnce);
        await MqttClient.SubscribeAsync($"boottorrent/cmd/machine/{ClientSettings.ClientIdentifier}/#", MqttQualityOfServiceLevel.AtLeastOnce);
        
        Interlocked.Increment(ref _connectionGeneration);
    }

    protected override Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs eventArgs)
    {
        MqttClient.ApplicationMessageReceivedAsync -= HandleMessageReceivedAsync;
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (MqttClient.IsConnected)
        {
            try
            {
                await PublishAsync(new MachineStoppedMessage
                {
                    IPAddress = NetworkHelper.GetPrimaryIPv4()?.ToString() ?? "UNKNOWN"
                }, EventFromMachine(MachineStoppedMessage.MessageType));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not publish the machine stop event.");
            }
        }
        await base.StopAsync(cancellationToken);
    }

    public MqttTopicContext EventFromMachine(string messageType)
    {
        return MqttTopicContext.CreateEventFromMachine(ClientSettings.ClientIdentifier, messageType);
    }

    public async Task SubscribeToZoneAsync(string zoneId, CancellationToken cancellationToken = default)
    {
        if (!MqttClient.IsConnected)
        {
            return;
        }

        await MqttClient.SubscribeAsync($"boottorrent/cmd/zone/{zoneId}/#", MqttQualityOfServiceLevel.AtLeastOnce, cancellationToken);
    }

    public async Task UnsubscribeFromZoneAsync(string zoneId, CancellationToken cancellationToken = default)
    {
        if (!MqttClient.IsConnected)
        {
            return;
        }

        await MqttClient.UnsubscribeAsync($"boottorrent/cmd/zone/{zoneId}/#", cancellationToken);
    }
}
