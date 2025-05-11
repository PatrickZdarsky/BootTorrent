using boottorrent_lib.communication;
using boottorrent_lib.communication.message;

namespace btclient;

public class ClientStatusWorker : BackgroundService
{
    private readonly ILogger<ClientStatusWorker> _logger;
    
    private readonly ClientMqttService _mqttService;

    public ClientStatusWorker(ILogger<ClientStatusWorker> logger, ClientMqttService mqttService)
    {
        _logger = logger;
        _mqttService = mqttService;
        
        _mqttService.MqttConnectionEstablished += async (sender, args) =>
        {
            await _mqttService.PublishAsync(new MachineStartedMessage() { IPAddress = "TEST" },
                _mqttService.EventFromMachine(MachineStartedMessage.MessageType));
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }

            await Task.Delay(1000, stoppingToken);
        }
    }
}