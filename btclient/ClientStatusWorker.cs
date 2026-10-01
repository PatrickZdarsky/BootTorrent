using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using btclient.artifact;
using btclient.torrent;

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
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var registeredGeneration = 0;
        try
        {
            await _torrentClient.StartAsync();
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_mqttService.IsConnected && _mqttService.ConnectionGeneration > 0)
                {
                    try
                    {
                        var generation = _mqttService.ConnectionGeneration;
                        if (registeredGeneration != generation)
                        {
                            await _machineConfigurationService.ResubscribeAsync(stoppingToken);
                            await _mqttService.PublishAsync(new MachineStartedMessage
                            {
                                IPAddress = NetworkHelper.GetPrimaryIPv4()?.ToString() ?? "UNKNOWN"
                            }, _mqttService.EventFromMachine(MachineStartedMessage.MessageType));
                            registeredGeneration = generation;
                        }

                        var status = _artifactRegistry.GetStatusSnapshot();
                        await _mqttService.PublishAsync(new MachineHeartbeatMessage
                        {
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            LoadedArtifacts = status.Loaded,
                            PendingArtifacts = status.Pending,
                            KnownArtifacts = status.Known,
                            ConfigHash = _machineConfigurationService.CurrentConfigHash
                        }, _mqttService.EventFromMachine(MachineHeartbeatMessage.MessageType));
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Client status publication failed; retrying.");
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            await _torrentClient.StopAsync();
        }
    }
}
