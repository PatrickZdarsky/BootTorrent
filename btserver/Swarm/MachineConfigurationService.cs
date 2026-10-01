using boottorrent_lib.client;
using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using btserver.Zone;
using btserver.Data;
using btserver.Config;
using btserver.torrent.monotorrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;

namespace btserver.Swarm;

/// <summary>Publishes zone membership to clients and replays assignments after configuration acknowledgement.</summary>
public class MachineConfigurationService(
    ILogger<MachineConfigurationService> logger,
    ZoneManager zoneManager,
    IServiceScopeFactory scopeFactory,
    Lazy<ServerMqttService> mqttService,
    IOptions<TorrentConfig> torrentConfig) : IMachineRegistryObserver
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
        // Election may have changed since the last heartbeat, even when zone membership did not.
        await EnsureConfigurationAsync(machine);
        if (!string.Equals(machine.ReportedConfigHash, machine.DesiredConfigHash, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.Equals(machine.LastAssignmentReconciliationHash, machine.DesiredConfigHash, StringComparison.Ordinal) ||
            DateTime.UtcNow - machine.LastAssignmentReconciliationUtc >= TimeSpan.FromSeconds(5))
        {
            await EnsureOutstandingAssignmentsAsync(machine);
        }
    }

    /// <summary>Replays missing assignments and removes stale artifacts after a configuration is acknowledged.</summary>
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
        var knownArtifactIds = machine.KnownArtifacts
            .Concat(machine.LoadedArtifacts)
            .Concat(machine.PendingArtifacts.Keys)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var artifactId in knownArtifactIds.Except(artifactIds, StringComparer.Ordinal))
            await publisher.PublishUnassignmentToMachineAsync(machine.Id, artifactId, CancellationToken.None);

        machine.LastAssignmentReconciliationHash = machine.DesiredConfigHash;
        machine.LastAssignmentReconciliationUtc = DateTime.UtcNow;
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

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BtDbContext>();
        var proxyArtifactIds = Guid.TryParse(membership.DistributionZoneId, out var zoneId)
            ? await db.ProxyDesignations.AsNoTracking()
                .Where(item => item.ZoneId == zoneId && item.MachineId == machine.Id)
                .Select(item => item.ArtifactId).ToListAsync(cancellationToken)
            : [];
        var originAddress = ResolveOriginAddress(torrentConfig.Value.TrackerUrl);
        var configuration = MachineConfiguration.Create(
            membership.ManagementZoneIds, membership.DistributionZoneId, proxyArtifactIds,
            originAddress, originAddress is null ? 0 : MonoTorrentSeederService.PeerPort);
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

    private static string? ResolveOriginAddress(string trackerUrl)
    {
        if (!Uri.TryCreate(trackerUrl, UriKind.Absolute, out var uri)) return null;
        if (IPAddress.TryParse(uri.Host, out var address)) return address.ToString();
        try
        {
            return Dns.GetHostAddresses(uri.Host)
                .FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetwork)?.ToString();
        }
        catch (SocketException)
        {
            return null;
        }
    }
}
