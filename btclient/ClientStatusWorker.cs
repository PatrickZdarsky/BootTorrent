using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using btclient.torrent;

namespace btclient;

public class ClientStatusWorker : BackgroundService
{
    private readonly ILogger<ClientStatusWorker> _logger;
    
    private readonly ClientMqttService _mqttService;
    private readonly ITorrentClient _torrentClient;

    public ClientStatusWorker(ILogger<ClientStatusWorker> logger, ClientMqttService mqttService, ITorrentClient torrentClient)
    {
        _logger = logger;
        _mqttService = mqttService;
        _torrentClient = torrentClient;
        
        
        _mqttService.MqttConnectionEstablished += async (sender, args) =>
        {
            await _mqttService.PublishAsync(new MachineStartedMessage() { IPAddress = "TEST" },
                _mqttService.EventFromMachine(MachineStartedMessage.MessageType));
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _torrentClient.StartAsync();
        _torrentClient.AddTorrentAsync("/home/patrick/boottorrent/artifacts/fee52392-13e1-454b-908c-d84622ae4de7/Test Artifact.torrent");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }

            await Task.Delay(1000, stoppingToken);
        }
        
        await _torrentClient.StopAsync();
    }
}