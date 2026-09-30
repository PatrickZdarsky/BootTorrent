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
        var membership = await zoneManager.GetMembershipAsync(machine, cancellationToken);
        await EnsureConfigurationAsync(machine, membership, cancellationToken);
    }

    public async Task EnsureConfigurationsAsync(IEnumerable<Machine> machines, CancellationToken cancellationToken = default)
    {
        var memberships = await zoneManager.GetMembershipsAsync(machines, cancellationToken);
        foreach (var (machine, membership) in memberships)
        {
            await EnsureConfigurationAsync(machine, membership, cancellationToken);
        }
    }

    private async Task EnsureConfigurationAsync(
        Machine machine,
        ZoneMembership membership,
        CancellationToken cancellationToken)
    {
        if (membership.IsAmbiguous)
        {
            logger.LogError("Machine {MachineId} matches multiple distribution zones: {ZoneIds}.", machine.Id, membership.ConflictingDistributionZoneIds);
        }

        var configuration = MachineConfiguration.Create(membership.ManagementZoneIds, membership.DistributionZoneId);
        machine.AssignedZones = configuration.AssignedZones;
        machine.DistributionZoneId = configuration.DistributionZoneId;
        machine.ConflictingDistributionZoneIds = membership.ConflictingDistributionZoneIds;
        machine.DesiredConfigHash = configuration.ConfigHash;

        if (string.Equals(machine.ReportedConfigHash, configuration.ConfigHash, StringComparison.Ordinal))
        {
            return;
        }

        logger.LogInformation(
            "Publishing machine configuration for machine {MachineId}. Desired hash: {ConfigHash}, reported hash: {ReportedHash}, assigned zones: {AssignedZones}, distribution zone: {DistributionZoneId}.",
            machine.Id,
            configuration.ConfigHash,
            machine.ReportedConfigHash,
            configuration.AssignedZones,
            configuration.DistributionZoneId);

        cancellationToken.ThrowIfCancellationRequested();
        await mqttService.Value.PublishAsync(
            new MachineConfigurationMessage { Configuration = configuration },
            MqttTopicContext.CreateCommandForMachine(machine.Id, MachineConfigurationMessage.MessageType));
    }
}
