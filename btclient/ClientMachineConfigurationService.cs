using boottorrent_lib.client;

namespace btclient;

public class ClientMachineConfigurationService(
    ILogger<ClientMachineConfigurationService> logger,
    ClientMqttService clientMqttService)
{
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public MachineConfiguration CurrentConfiguration { get; private set; } = MachineConfiguration.Create([]);

    public string CurrentConfigHash => CurrentConfiguration.ConfigHash;

    public async Task ApplyConfigurationAsync(MachineConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var normalizedConfiguration = MachineConfiguration.Create(configuration.AssignedZones);

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var previousZones = CurrentConfiguration.AssignedZones.ToHashSet(StringComparer.Ordinal);
            var desiredZones = normalizedConfiguration.AssignedZones.ToHashSet(StringComparer.Ordinal);

            foreach (var zoneId in desiredZones.Except(previousZones, StringComparer.Ordinal))
            {
                await clientMqttService.SubscribeToZoneAsync(zoneId, cancellationToken);
            }

            foreach (var zoneId in previousZones.Except(desiredZones, StringComparer.Ordinal))
            {
                await clientMqttService.UnsubscribeFromZoneAsync(zoneId, cancellationToken);
            }

            CurrentConfiguration = normalizedConfiguration;
        }
        finally
        {
            _mutex.Release();
        }

        logger.LogInformation(
            "Applied machine configuration hash {ConfigHash}. Assigned zones: {AssignedZones}.",
            CurrentConfiguration.ConfigHash,
            CurrentConfiguration.AssignedZones);
    }

    public async Task ResubscribeAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            foreach (var zoneId in CurrentConfiguration.AssignedZones)
            {
                await clientMqttService.SubscribeToZoneAsync(zoneId, cancellationToken);
            }
        }
        finally
        {
            _mutex.Release();
        }
    }
}
