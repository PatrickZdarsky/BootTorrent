using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using btclient.torrent;
using btclient.torrent.monotorrent;

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
        //_torrentClient.AddTorrentAsync("/home/patrick/boottorrent/artifacts/575fbabf-d63a-460c-b3d5-24f53c2cd4cd/TestArtifact.torrent");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            var manager = ((MonoTorrentClient)_torrentClient).engine.Torrents.FirstOrDefault();
            if (manager is not null)
            {
                //Log percentage downloaded and stats
                _logger.LogInformation("Torrent {TorrentName} with status {Status} is {Progress}% downloaded. Download speed: {DownloadSpeed} MB/s, Upload speed: {UploadSpeed} MB/s",
                    manager.Torrent.Name, manager.State, manager.Progress, manager.Monitor.DownloadSpeed / 1024 / 1024, manager.Monitor.UploadSpeed / 1024 / 1024);
            }

            await Task.Delay(1000, stoppingToken);
        }
        
        await _torrentClient.StopAsync();
    }
}