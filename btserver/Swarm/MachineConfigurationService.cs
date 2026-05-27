using boottorrent_lib.client;
using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using btserver.Data;
using Microsoft.EntityFrameworkCore;

namespace btserver.Swarm;

public class MachineConfigurationService(
    ILogger<MachineConfigurationService> logger,
    IServiceScopeFactory serviceScopeFactory,
    Lazy<ServerMqttService> mqttService)
{
    public async Task EnsureConfigurationAsync(Machine machine, CancellationToken cancellationToken = default)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BtDbContext>();
        var zones = await dbContext.Zones
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        await EnsureConfigurationAsync(machine, zones, cancellationToken);
    }

    public async Task EnsureConfigurationsAsync(IEnumerable<Machine> machines, CancellationToken cancellationToken = default)
    {
        var machineList = machines.ToList();
        if (machineList.Count == 0)
        {
            return;
        }

        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BtDbContext>();
        var zones = await dbContext.Zones
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var machine in machineList)
        {
            await EnsureConfigurationAsync(machine, zones, cancellationToken);
        }
    }

    private async Task EnsureConfigurationAsync(
        Machine machine,
        IReadOnlyCollection<Zone.Zone> zones,
        CancellationToken cancellationToken)
    {
        var configuration = BuildConfiguration(machine, zones);
        machine.AssignedZones = configuration.AssignedZones;
        machine.DesiredConfigHash = configuration.ConfigHash;

        if (string.Equals(machine.ReportedConfigHash, configuration.ConfigHash, StringComparison.Ordinal))
        {
            return;
        }

        logger.LogInformation(
            "Publishing machine configuration for machine {MachineId}. Desired hash: {ConfigHash}, reported hash: {ReportedHash}, assigned zones: {AssignedZones}.",
            machine.Id,
            configuration.ConfigHash,
            machine.ReportedConfigHash,
            configuration.AssignedZones);

        await mqttService.Value.PublishAsync(
            new MachineConfigurationMessage
            {
                Configuration = configuration
            },
            MqttTopicContext.CreateCommandForMachine(machine.Id, MachineConfigurationMessage.MessageType));
    }

    private static MachineConfiguration BuildConfiguration(Machine machine, IEnumerable<Zone.Zone> zones)
    {
        var assignedZones = zones
            .Where(zone => zone.Contains(machine))
            .Select(zone => zone.Id.ToString())
            .ToList();

        return MachineConfiguration.Create(assignedZones);
    }
}
