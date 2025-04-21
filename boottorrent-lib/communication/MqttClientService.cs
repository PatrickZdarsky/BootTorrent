using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;

namespace boottorrent_lib.communication;

// Taken from https://github.com/rafiulgits/mqtt-client-dotnet-core
public abstract class MqttClientService : BackgroundService
{
    public readonly IMqttClient MqttClient;
    private readonly MqttClientOptions _options;
    private readonly ILogger _logger;

    protected MqttClientService(MqttClientOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger;
        MqttClient = new MqttFactory().CreateMqttClient();
        MqttClient.ConnectedAsync += HandleConnectedAsync;
        MqttClient.DisconnectedAsync += HandleDisconnectedAsync;
    }
    
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // This code will also do the very first connect! So no call to _ConnectAsync_ is required in the first place.
                if (await MqttClient.TryPingAsync(cancellationToken)) continue;
                        
                await MqttClient.ConnectAsync(_options, CancellationToken.None);

                // Subscribe to topics when session is clean etc.
                _logger.LogInformation("The MQTT client is connected.");
            }
            catch (Exception ex)
            {
                // Handle the exception properly (logging etc.).
                _logger.LogError(ex, "The MQTT client connection failed");
            }
            finally
            {
                // Check the connection state every 5 seconds and perform a reconnect if required.
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (MqttClient.IsConnected)
        {
            _logger.LogInformation("Disconnecting MQTT client...");
            var disconnectOption = new MqttClientDisconnectOptions
            {
                Reason = MqttClientDisconnectOptionsReason.NormalDisconnection
            };
            await MqttClient.DisconnectAsync(disconnectOption, cancellationToken);
        }

        await base.StopAsync(cancellationToken);
    }

    protected abstract Task HandleConnectedAsync(MqttClientConnectedEventArgs eventArgs);

    protected abstract Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs eventArgs);
}