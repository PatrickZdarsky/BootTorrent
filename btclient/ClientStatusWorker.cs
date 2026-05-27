using boottorrent_lib.artifact;
using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using btclient.artifact;
using btclient.torrent;
using btclient.torrent.monotorrent;

namespace btclient;

public class ClientStatusWorker : BackgroundService
{
    private readonly ILogger<ClientStatusWorker> _logger;
    
    private readonly ClientMqttService _mqttService;
    private readonly ClientMachineConfigurationService _machineConfigurationService;
    private readonly ArtifactRegistry _artifactRegistry;
    private readonly ITorrentClient _torrentClient;

    public ClientStatusWorker(
        ILogger<ClientStatusWorker> logger,
        ClientMqttService mqttService,
        ClientMachineConfigurationService machineConfigurationService,
        ArtifactRegistry artifactRegistry,
        ITorrentClient torrentClient)
    {
        _logger = logger;
        _mqttService = mqttService;
        _machineConfigurationService = machineConfigurationService;
        _artifactRegistry = artifactRegistry;
        _torrentClient = torrentClient;
        
        _artifactRegistry.LoadExistingArtifacts();
        
        _mqttService.MqttConnectionEstablished += async (sender, args) =>
        {
            await _machineConfigurationService.ResubscribeAsync();
            await _mqttService.PublishAsync(new MachineStartedMessage { IPAddress = NetworkHelper.GetPrimaryIPv4()?.ToString() ?? "UNKNOWN" },
                _mqttService.EventFromMachine(MachineStartedMessage.MessageType));
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _torrentClient.StartAsync();
        
        while (!stoppingToken.IsCancellationRequested)
        {
            await _mqttService.PublishAsync(new MachineHeartbeatMessage()
            { 
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                LoadedArtifacts = _artifactRegistry.Artifacts.Where(a => a.State == ClientHostedArtifact.ArtifactState.Ready).Select(a => a.ID).ToList(),
                PendingArtifacts = _artifactRegistry.ActiveJobs.ToDictionary(a => a.TorrentJob.ArtifactId, a => a.PercentageComplete),
                ConfigHash = _machineConfigurationService.CurrentConfigHash
            }, _mqttService.EventFromMachine(MachineHeartbeatMessage.MessageType));

            await Task.Delay(1000, stoppingToken);
        }
        
        await _torrentClient.StopAsync();
    }
}
