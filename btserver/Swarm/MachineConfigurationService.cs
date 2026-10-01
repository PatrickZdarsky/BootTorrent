using boottorrent_lib.client;
using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using btserver.Zone;
using btserver.Data;
using Microsoft.EntityFrameworkCore;

namespace btserver.Swarm;

/// <summary>Publishes zone membership to clients and replays assignments after configuration acknowledgement.</summary>
public class MachineConfigurationService(
    ILogger<MachineConfigurationService> logger,
    ZoneManager zoneManager,
    IServiceScopeFactory scopeFactory,
    Lazy<ServerMqttService> mqttService) : IMachineRegistryObserver
{
    /// <summary>Publishes the registered machine's current zone configuration when needed.</summary>
    /// <param name="machine">The registered machine.</param>
    /// <returns>A task for the configuration update.</returns>
    public Task OnMachineRegisteredAsync(Machine machine) => EnsureConfigurationAsync(machine);

    /// <summary>Retries an unacknowledged configuration, or replays missing assignments after acknowledgement.</summary>
    /// <param name="machine">The machine that reported a heartbeat.</param>
    /// <returns>A task for any required publications.</returns>
    public async Task OnMachineHeartbeatAsync(Machine machine)
    {
        if (!string.Equals(machine.ReportedConfigHash, machine.DesiredConfigHash, StringComparison.Ordinal))
        {
            await EnsureConfigurationAsync(machine);
            return;
        }

        if (!string.Equals(machine.LastAssignmentReconciliationHash, machine.DesiredConfigHash, StringComparison.Ordinal))
        {
            await EnsureOutstandingAssignmentsAsync(machine);
        }
    }

    /// <summary>Replays assignments missing from an acknowledged machine configuration once per desired hash.</summary>
    /// <param name="machine">The machine whose loaded and pending artifacts are checked.</param>
    /// <returns>A task for any machine-scoped assignment publications.</returns>
    private async Task EnsureOutstandingAssignmentsAsync(Machine machine)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BtDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IArtifactAssignmentPublisher>();
        var zones = await db.Zones.AsNoTracking().ToListAsync();
        var artifactIds = zones.Where(zone => zone.Contains(machine))
            .SelectMany(zone => zone.AssignedArtifactIds)
            .ToHashSet(StringComparer.Ordinal);
        if (machine.DistributionZoneId == ZoneManager.DefaultDistributionZoneId.ToString())
        {
            artifactIds.UnionWith(await db.DefaultZoneArtifactAssignments.AsNoTracking()
                .Select(assignment => assignment.ArtifactId).ToListAsync());
        }

        foreach (var artifactId in artifactIds)
        {
            if (machine.LoadedArtifacts.Contains(artifactId, StringComparer.Ordinal) ||
                machine.PendingArtifacts.ContainsKey(artifactId)) continue;
            await publisher.PublishAssignmentToMachineAsync(machine.Id, artifactId, CancellationToken.None);
        }
        machine.LastAssignmentReconciliationHash = machine.DesiredConfigHash;
    }

    /// <summary>Recomputes and publishes one machine's desired zone configuration if its reported hash differs.</summary>
    /// <param name="machine">The machine to update.</param>
    /// <param name="cancellationToken">Cancels zone loading and publication.</param>
    /// <returns>A task for the update.</returns>
    public async Task EnsureConfigurationAsync(Machine machine, CancellationToken cancellationToken = default)
    {
        var membership = await zoneManager.GetMembershipAsync(machine, cancellationToken);
        await EnsureConfigurationAsync(machine, membership, cancellationToken);
    }

    /// <summary>Recomputes configurations for several machines from one zone snapshot.</summary>
    /// <param name="machines">Machines to update.</param>
    /// <param name="cancellationToken">Cancels zone loading and publication.</param>
    /// <returns>A task for all updates.</returns>
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
