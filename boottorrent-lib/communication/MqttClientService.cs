using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;

namespace boottorrent_lib.communication;

// Taken from https://github.com/rafiulgits/mqtt-client-dotnet-core
public class MqttClientService : IMqttClientService
{
    public readonly IMqttClient MqttClient;
    private readonly MqttClientOptions _options;
    private readonly ILogger<MqttClientService> _logger;
    
    public MqttClientService(MqttClientOptions options, ILogger<MqttClientService> logger)
    {
        _options = options;
        _logger = logger;
        MqttClient = new MqttFactory().CreateMqttClient();
        ConfigureMqttClient();
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(
            async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // This code will also do the very first connect! So no call to _ConnectAsync_ is required in the first place.
                        if (await MqttClient.TryPingAsync(cancellationToken)) continue;
                        
                        await MqttClient.ConnectAsync(MqttClient.Options, CancellationToken.None);

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
                        await Task.Delay(TimeSpan.FromSeconds(5));
                    }
                }
            });
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            var disconnectOption = new MqttClientDisconnectOptions
            {
                Reason = MqttClientDisconnectOptionsReason.NormalDisconnection,
                ReasonString = "NormalDiconnection"
            };
            await MqttClient.DisconnectAsync(disconnectOption, cancellationToken);
        }
        await MqttClient.DisconnectAsync();
    }
    
    private void ConfigureMqttClient()
    {
        MqttClient.ConnectedAsync += HandleConnectedAsync;
        MqttClient.DisconnectedAsync += HandleDisconnectedAsync;
    }

    private async Task HandleConnectedAsync(MqttClientConnectedEventArgs eventArgs)
    {
        _logger.LogInformation("connected");
        await MqttClient.SubscribeAsync("hello/world");
    }

    private async Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs eventArgs)
    {

        _logger.LogInformation("HandleDisconnected");
        await Task.CompletedTask;
    }
}