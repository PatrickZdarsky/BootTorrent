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
        using var publisherCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        Task? completionPublisher = null;
        try
        {
            await _torrentClient.StartAsync();
            completionPublisher = PublishCompletionsAsync(publisherCancellation.Token);
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
            await publisherCancellation.CancelAsync();
            if (completionPublisher is not null) await completionPublisher;
            await _torrentClient.StopAsync();
        }
    }

    private async Task PublishCompletionsAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var completion in _artifactRegistry.CompletedDownloads.ReadAllAsync(stoppingToken))
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    if (!_mqttService.IsConnected)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                        continue;
                    }
                    try
                    {
                        await _mqttService.PublishAsync(completion,
                            _mqttService.EventFromMachine(ArtifactDownloadCompletedMessage.MessageType));
                        break;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Completion event for artifact {ArtifactId} could not be published; retrying.", completion.ArtifactId);
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
