using boottorrent_lib.client;
using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using btserver.Zone;

namespace btserver.Swarm;

public class MachineConfigurationService(
    ILogger<MachineConfigurationService> logger,
    ZoneManager zoneManager,
    Lazy<ServerMqttService> mqttService) : IMachineRegistryObserver
{
    public Task OnMachineRegisteredAsync(Machine machine) => EnsureConfigurationAsync(machine);

    public Task OnMachineHeartbeatAsync(Machine machine)
    {
        return string.Equals(machine.ReportedConfigHash, machine.DesiredConfigHash, StringComparison.Ordinal)
            ? Task.CompletedTask
            : EnsureConfigurationAsync(machine);
    }

    public async Task EnsureConfigurationAsync(Machine machine, CancellationToken cancellationToken = default)
    {
        var assignedZoneIds = await zoneManager.GetAssignedZoneIdsAsync(machine, cancellationToken);
        await EnsureConfigurationAsync(machine, assignedZoneIds, cancellationToken);
    }

    public async Task EnsureConfigurationsAsync(IEnumerable<Machine> machines, CancellationToken cancellationToken = default)
    {
        var memberships = await zoneManager.GetAssignedZoneIdsAsync(machines, cancellationToken);
        foreach (var (machine, assignedZoneIds) in memberships)
        {
            await EnsureConfigurationAsync(machine, assignedZoneIds, cancellationToken);
        }
    }

    private async Task EnsureConfigurationAsync(
        Machine machine,
        IEnumerable<string> assignedZoneIds,
        CancellationToken cancellationToken)
    {
        var configuration = MachineConfiguration.Create(assignedZoneIds);
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

        cancellationToken.ThrowIfCancellationRequested();
        await mqttService.Value.PublishAsync(
            new MachineConfigurationMessage { Configuration = configuration },
            MqttTopicContext.CreateCommandForMachine(machine.Id, MachineConfigurationMessage.MessageType));
    }
}
