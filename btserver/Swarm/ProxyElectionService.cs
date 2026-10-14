using boottorrent_lib.client;
using btserver.Data;
using btserver.Zone;
using Microsoft.EntityFrameworkCore;

namespace btserver.Swarm;

/// <summary>Persists and reconciles proxy designations for each distribution zone and artifact.</summary>
public sealed class ProxyElectionService(
    IServiceScopeFactory scopeFactory,
    IActiveMachineSource machineSource,
    IEnumerable<IProxySelectionPolicy> selectionPolicies,
    ILogger<ProxyElectionService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IReadOnlyDictionary<string, IProxySelectionPolicy> _policies =
        selectionPolicies.ToDictionary(policy => policy.Key, StringComparer.Ordinal);

    /// <summary>Reconciles assignments in every distribution zone, including assignments inherited from management zones and existing election states.</summary>
    /// <param name="cancellationToken">Cancels database operations and reconciliation.</param>
    public async Task ReconcileAllAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BtDbContext>();
        var zones = await db.Zones.AsNoTracking().ToListAsync(cancellationToken);
        var assignments = zones
            .Where(zone => zone.IsDistributionZone)
            .SelectMany(zone => zone.AssignedArtifactIds.Select(artifactId => (zone.Id, artifactId)))
            .ToList();
        foreach (var machine in machineSource.GetActiveMachines())
        {
            var membership = ZoneManager.Resolve(machine, zones);
            if (!Guid.TryParse(membership.DistributionZoneId, out var distributionZoneId)) continue;
            assignments.AddRange(zones
                .Where(zone => zone.Contains(machine))
                .SelectMany(zone => zone.AssignedArtifactIds.Select(artifactId => (distributionZoneId, artifactId))));
        }
        assignments.AddRange((await db.DefaultZoneArtifactAssignments.AsNoTracking().ToListAsync(cancellationToken))
            .Select(assignment => (ZoneManager.DefaultDistributionZoneId, assignment.ArtifactId)));
        var existing = await db.ProxyElectionStates.AsNoTracking()
            .Select(state => new { state.ZoneId, state.ArtifactId })
            .ToListAsync(cancellationToken);
        assignments.AddRange(existing.Select(state => (state.ZoneId, state.ArtifactId)));

        foreach (var (zoneId, artifactId) in assignments.Distinct())
        {
            await ReconcileAsync(zoneId, artifactId, cancellationToken);
        }
    }

    /// <summary>Removes ineligible proxies and applies initial, count-change, or recovery election rules for one artifact.</summary>
    /// <remarks>Only active machines that have reported a heartbeat and are assigned the artifact can be selected. A complete copy suppresses automatic selection.</remarks>
    /// <param name="zoneId">The distribution zone ID, including the default zone ID.</param>
    /// <param name="artifactId">The assigned artifact ID.</param>
    /// <param name="cancellationToken">Cancels database operations and reconciliation.</param>
    public async Task ReconcileAsync(Guid zoneId, string artifactId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BtDbContext>();
            var zones = await db.Zones.AsNoTracking().ToListAsync(cancellationToken);
            var zone = zones.FirstOrDefault(candidate => candidate.Id == zoneId);
            var isDefault = zoneId == ZoneManager.DefaultDistributionZoneId;
            var directlyAssigned = isDefault
                ? await db.DefaultZoneArtifactAssignments.AnyAsync(item => item.ArtifactId == artifactId, cancellationToken)
                : zone is { IsDistributionZone: true } && zone.AssignedArtifactIds.Contains(artifactId, StringComparer.Ordinal);

            var state = await db.ProxyElectionStates
                .FirstOrDefaultAsync(item => item.ZoneId == zoneId && item.ArtifactId == artifactId, cancellationToken);
            var designations = await db.ProxyDesignations
                .Where(item => item.ZoneId == zoneId && item.ArtifactId == artifactId)
                .OrderBy(item => item.SelectedAtUtc)
                .ThenBy(item => item.MachineId)
                .ToListAsync(cancellationToken);

            var zoneMachines = machineSource.GetActiveMachines()
                .Where(machine => ZoneManager.Resolve(machine, zones).DistributionZoneId == zoneId.ToString())
                .ToList();
            var defaultAssigned = await db.DefaultZoneArtifactAssignments
                .AnyAsync(item => item.ArtifactId == artifactId, cancellationToken);
            var assignedMachines = zoneMachines.Where(machine =>
                (isDefault && defaultAssigned) ||
                zones.Any(candidate => candidate.Contains(machine) &&
                    candidate.AssignedArtifactIds.Contains(artifactId, StringComparer.Ordinal)))
                .ToList();

            if (!directlyAssigned && assignedMachines.Count == 0)
            {
                foreach (var designation in designations)
                    Remove(db, state, designation, "artifact-unassigned");
                if (state is not null) db.ProxyElectionStates.Remove(state);
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            var configuration = await db.ProxyZoneConfigurations.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ZoneId == zoneId, cancellationToken)
                ?? new ProxyZoneConfiguration { ZoneId = zoneId };
            if (!_policies.TryGetValue(configuration.SelectionPolicyKey, out var selectionPolicy))
                throw new InvalidOperationException($"Unknown proxy selection policy '{configuration.SelectionPolicyKey}'.");

            var eligible = assignedMachines
                .Where(machine => machine.HasReportedStatus)
                .OrderBy(machine => machine.Id, StringComparer.Ordinal)
                .ToList();
            var eligibleIds = eligible.Select(machine => machine.Id).ToHashSet(StringComparer.Ordinal);
            var hasCompleteCopy = eligible.Any(machine => machine.LoadedArtifacts.Contains(artifactId, StringComparer.Ordinal));

            state ??= new ProxyElectionState { ZoneId = zoneId, ArtifactId = artifactId };
            if (db.Entry(state).State == EntityState.Detached) db.ProxyElectionStates.Add(state);

            foreach (var designation in designations.Where(item => !eligibleIds.Contains(item.MachineId)).ToList())
            {
                Remove(db, state, designation, "machine-unavailable-or-zone-changed");
                designations.Remove(designation);
            }

            if (!state.Initialized && eligible.Count > 0)
            {
                state.Initialized = true;
                state.AppliedInitialCount = configuration.DesiredInitialCount;
                if (!hasCompleteCopy)
                    AddCandidates(db, state, designations, eligible, configuration, selectionPolicy,
                        configuration.DesiredInitialCount, "initial");
            }
            else if (state.Initialized && state.AppliedInitialCount != configuration.DesiredInitialCount)
            {
                state.AppliedInitialCount = configuration.DesiredInitialCount;
                while (designations.Count > configuration.DesiredInitialCount)
                {
                    var removed = designations[^1];
                    Remove(db, state, removed, "count-decreased");
                    designations.RemoveAt(designations.Count - 1);
                }
                if (!hasCompleteCopy)
                    AddCandidates(db, state, designations, eligible, configuration, selectionPolicy,
                        configuration.DesiredInitialCount - designations.Count, "count-increased");
            }
            else if (state.Initialized && designations.Count == 0 && !hasCompleteCopy)
            {
                AddCandidates(db, state, designations, eligible, configuration, selectionPolicy, 1, "recovery");
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Checks whether a machine currently has a persisted proxy designation for an artifact in a distribution zone.</summary>
    /// <param name="zoneId">The distribution zone ID.</param>
    /// <param name="artifactId">The artifact ID.</param>
    /// <param name="machineId">The machine ID.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns><see langword="true"/> if the designation exists.</returns>
    public async Task<bool> IsDesignatedAsync(Guid zoneId, string artifactId, string machineId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BtDbContext>();
        return await db.ProxyDesignations.AsNoTracking().AnyAsync(
            item => item.ZoneId == zoneId && item.ArtifactId == artifactId && item.MachineId == machineId,
            cancellationToken);
    }

    /// <summary>Checks for a direct assignment or an assignment inherited by an active member from a management zone.</summary>
    /// <param name="zoneId">The distribution zone ID.</param>
    /// <param name="artifactId">The artifact ID.</param>
    /// <param name="cancellationToken">Cancels database operations.</param>
    /// <returns><see langword="true"/> when the artifact is assigned within the distribution zone.</returns>
    public async Task<bool> IsArtifactAssignedAsync(Guid zoneId, string artifactId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BtDbContext>();
        if (zoneId == ZoneManager.DefaultDistributionZoneId &&
            await db.DefaultZoneArtifactAssignments.AnyAsync(item => item.ArtifactId == artifactId, cancellationToken))
            return true;
        var zones = await db.Zones.AsNoTracking().ToListAsync(cancellationToken);
        var zone = zones.FirstOrDefault(item => item.Id == zoneId);
        if (zoneId != ZoneManager.DefaultDistributionZoneId && zone is not { IsDistributionZone: true }) return false;
        if (zone is not null && zone.AssignedArtifactIds.Contains(artifactId, StringComparer.Ordinal)) return true;
        return machineSource.GetActiveMachines().Any(machine =>
            ZoneManager.Resolve(machine, zones).DistributionZoneId == zoneId.ToString() &&
            zones.Any(candidate => candidate.Contains(machine) &&
                candidate.AssignedArtifactIds.Contains(artifactId, StringComparer.Ordinal)));
    }

    /// <summary>Checks whether a machine receives an artifact through a matching zone or the default distribution zone.</summary>
    /// <param name="machine">The machine whose current zone membership is evaluated.</param>
    /// <param name="artifactId">The artifact ID.</param>
    /// <param name="cancellationToken">Cancels database operations.</param>
    /// <returns><see langword="false"/> if the machine has ambiguous distribution membership or no applicable assignment.</returns>
    public async Task<bool> IsMachineAssignedAsync(Machine machine, string artifactId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BtDbContext>();
        var zones = await db.Zones.AsNoTracking().ToListAsync(cancellationToken);
        var membership = ZoneManager.Resolve(machine, zones);
        if (membership.DistributionZoneId is null) return false;
        if (zones.Any(zone => zone.Contains(machine) && zone.AssignedArtifactIds.Contains(artifactId, StringComparer.Ordinal)))
            return true;
        return membership.DistributionZoneId == ZoneManager.DefaultDistributionZoneId.ToString() &&
               await db.DefaultZoneArtifactAssignments.AnyAsync(item => item.ArtifactId == artifactId, cancellationToken);
    }

    /// <summary>Gets a distribution zone's proxy settings, using defaults when no settings have been persisted.</summary>
    /// <param name="zoneId">The distribution zone ID.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns>The settings, or <see langword="null"/> if the distribution zone does not exist.</returns>
    public async Task<ProxyZoneConfiguration?> GetConfigurationAsync(Guid zoneId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BtDbContext>();
        if (!await ZoneExistsAsync(db, zoneId, cancellationToken)) return null;
        return await db.ProxyZoneConfigurations.AsNoTracking().FirstOrDefaultAsync(item => item.ZoneId == zoneId, cancellationToken)
            ?? new ProxyZoneConfiguration { ZoneId = zoneId };
    }

    /// <summary>Stores proxy settings for a distribution zone and reconciles affected elections.</summary>
    /// <param name="zoneId">The distribution zone ID.</param>
    /// <param name="desiredInitialCount">The positive number of proxies to select initially.</param>
    /// <param name="selectionPolicyKey">The key of a registered selection policy.</param>
    /// <param name="preferredMachineIds">Machine IDs to prioritize, in order; blanks and duplicates are discarded.</param>
    /// <param name="cancellationToken">Cancels database operations and reconciliation.</param>
    /// <returns><see langword="false"/> if the distribution zone does not exist.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="desiredInitialCount"/> is less than one.</exception>
    /// <exception cref="ArgumentException"><paramref name="selectionPolicyKey"/> is unknown.</exception>
    public async Task<bool> UpdateConfigurationAsync(
        Guid zoneId, int desiredInitialCount, string selectionPolicyKey,
        IEnumerable<string>? preferredMachineIds, CancellationToken cancellationToken = default)
    {
        if (desiredInitialCount < 1) throw new ArgumentOutOfRangeException(nameof(desiredInitialCount));
        if (!_policies.ContainsKey(selectionPolicyKey)) throw new ArgumentException("Unknown proxy selection policy.", nameof(selectionPolicyKey));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BtDbContext>();
            if (!await ZoneExistsAsync(db, zoneId, cancellationToken)) return false;
            var configuration = await db.ProxyZoneConfigurations.FirstOrDefaultAsync(item => item.ZoneId == zoneId, cancellationToken);
            if (configuration is null)
            {
                configuration = new ProxyZoneConfiguration { ZoneId = zoneId };
                db.ProxyZoneConfigurations.Add(configuration);
            }
            configuration.DesiredInitialCount = desiredInitialCount;
            configuration.SelectionPolicyKey = selectionPolicyKey;
            configuration.PreferredMachineIds = (preferredMachineIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
        await ReconcileAllAsync(cancellationToken);
        return true;
    }

    /// <summary>Gets persisted proxy designations for an artifact in selection order.</summary>
    /// <param name="zoneId">The distribution zone ID.</param>
    /// <param name="artifactId">The artifact ID.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns>The current designations, oldest first.</returns>
    public async Task<IReadOnlyList<ProxyDesignation>> GetDesignationsAsync(Guid zoneId, string artifactId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BtDbContext>();
        return await db.ProxyDesignations.AsNoTracking()
            .Where(item => item.ZoneId == zoneId && item.ArtifactId == artifactId)
            .OrderBy(item => item.SelectedAtUtc)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Replaces the proxy set selected by an operator and records designation transitions.</summary>
    /// <param name="zoneId">The distribution zone ID.</param>
    /// <param name="artifactId">The artifact ID.</param>
    /// <param name="machineIds">Distinct, nonempty IDs of active, reporting machines assigned this artifact in the zone.</param>
    /// <param name="cancellationToken">Cancels database operations.</param>
    /// <exception cref="ArgumentException">The zone or selected machines are invalid, or the selection exceeds the configured count.</exception>
    public async Task SetDesignationsAsync(Guid zoneId, string artifactId, IReadOnlyCollection<string> machineIds, CancellationToken cancellationToken = default)
    {
        if (machineIds.Count == 0 || machineIds.Count != machineIds.Distinct(StringComparer.Ordinal).Count())
            throw new ArgumentException("Provide distinct, nonempty machine IDs.", nameof(machineIds));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BtDbContext>();
            var zones = await db.Zones.AsNoTracking().ToListAsync(cancellationToken);
            if (zoneId != ZoneManager.DefaultDistributionZoneId &&
                !zones.Any(zone => zone.Id == zoneId && zone.IsDistributionZone))
                throw new ArgumentException("Distribution zone not found.", nameof(zoneId));

            var configuration = await db.ProxyZoneConfigurations.AsNoTracking()
                .FirstOrDefaultAsync(item => item.ZoneId == zoneId, cancellationToken)
                ?? new ProxyZoneConfiguration { ZoneId = zoneId };
            if (machineIds.Count > configuration.DesiredInitialCount)
                throw new ArgumentException("More machine IDs were supplied than the configured initial count.", nameof(machineIds));

            var defaultAssigned = zoneId == ZoneManager.DefaultDistributionZoneId &&
                await db.DefaultZoneArtifactAssignments.AnyAsync(item => item.ArtifactId == artifactId, cancellationToken);
            var eligible = machineSource.GetActiveMachines().Where(machine =>
                machine.HasReportedStatus && ZoneManager.Resolve(machine, zones).DistributionZoneId == zoneId.ToString() &&
                (defaultAssigned || zones.Any(zone => zone.Contains(machine) &&
                    zone.AssignedArtifactIds.Contains(artifactId, StringComparer.Ordinal))))
                .Select(machine => machine.Id).ToHashSet(StringComparer.Ordinal);
            if (machineIds.Any(id => !eligible.Contains(id)))
                throw new ArgumentException("Every selected machine must be online and assigned the artifact in this distribution zone.", nameof(machineIds));

            var state = await db.ProxyElectionStates.FirstOrDefaultAsync(
                item => item.ZoneId == zoneId && item.ArtifactId == artifactId, cancellationToken);
            if (state is null)
            {
                state = new ProxyElectionState { ZoneId = zoneId, ArtifactId = artifactId };
                db.ProxyElectionStates.Add(state);
            }
            state.Initialized = true;
            state.AppliedInitialCount = configuration.DesiredInitialCount;
            var existing = await db.ProxyDesignations.Where(item => item.ZoneId == zoneId && item.ArtifactId == artifactId)
                .ToListAsync(cancellationToken);
            foreach (var designation in existing.Where(item => !machineIds.Contains(item.MachineId, StringComparer.Ordinal)))
                Remove(db, state, designation, "operator-change");
            var existingIds = existing.Select(item => item.MachineId).ToHashSet(StringComparer.Ordinal);
            var ordinal = 0;
            foreach (var machineId in machineIds.Where(id => !existingIds.Contains(id)))
            {
                var now = DateTime.UtcNow.AddTicks(ordinal++);
                db.ProxyDesignations.Add(new ProxyDesignation
                {
                    ZoneId = zoneId, ArtifactId = artifactId, MachineId = machineId,
                    SelectedAtUtc = now, Reason = "operator"
                });
                state.Generation++;
                db.ProxyElectionTransitions.Add(new ProxyElectionTransition
                {
                    ZoneId = zoneId, ArtifactId = artifactId, MachineId = machineId,
                    OccurredAtUtc = now, Action = "designated", Reason = "operator", Generation = state.Generation
                });
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Gets proxy transitions in stable ascending ID order after the supplied cursor.</summary>
    /// <param name="zoneId">The distribution zone ID.</param>
    /// <param name="artifactId">The artifact ID.</param>
    /// <param name="cancellationToken">Cancels the database query.</param>
    /// <returns>Transitions ordered from newest to oldest.</returns>
    public async Task<IReadOnlyList<ProxyElectionTransition>> GetTransitionsAsync(
        Guid zoneId, string artifactId, long afterId = 0, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BtDbContext>();
        return await db.ProxyElectionTransitions.AsNoTracking()
            .Where(item => item.ZoneId == zoneId && item.ArtifactId == artifactId && item.Id > afterId)
            .OrderBy(item => item.Id).Take(limit).ToListAsync(cancellationToken);
    }

    private static async Task<bool> ZoneExistsAsync(BtDbContext db, Guid zoneId, CancellationToken cancellationToken) =>
        zoneId == ZoneManager.DefaultDistributionZoneId ||
        await db.Zones.AnyAsync(zone => zone.Id == zoneId && zone.IsDistributionZone, cancellationToken);

    private void AddCandidates(
        BtDbContext db, ProxyElectionState state, List<ProxyDesignation> designations,
        IReadOnlyCollection<Machine> eligible, ProxyZoneConfiguration configuration,
        IProxySelectionPolicy selectionPolicy, int count, string reason)
    {
        if (count <= 0) return;
        var now = DateTime.UtcNow;
        var ordinal = 0;
        var incumbents = designations.Select(item => item.MachineId).ToList();
        foreach (var machine in selectionPolicy.Select(eligible, incumbents, configuration.PreferredMachineIds, count))
        {
            var designation = new ProxyDesignation
            {
                ZoneId = state.ZoneId, ArtifactId = state.ArtifactId, MachineId = machine.Id,
                SelectedAtUtc = now.AddTicks(ordinal++), Reason = reason
            };
            db.ProxyDesignations.Add(designation);
            designations.Add(designation);
            state.Generation++;
            db.ProxyElectionTransitions.Add(new ProxyElectionTransition
            {
                ZoneId = state.ZoneId, ArtifactId = state.ArtifactId, MachineId = machine.Id,
                OccurredAtUtc = now, Action = "designated", Reason = reason, Generation = state.Generation
            });
            logger.LogInformation("Designated machine {MachineId} as proxy for zone {ZoneId}, artifact {ArtifactId}: {Reason}.",
                machine.Id, state.ZoneId, state.ArtifactId, reason);
        }
    }

    private static void Remove(BtDbContext db, ProxyElectionState? state, ProxyDesignation designation, string reason)
    {
        db.ProxyDesignations.Remove(designation);
        if (state is null) return;
        state.Generation++;
        db.ProxyElectionTransitions.Add(new ProxyElectionTransition
        {
            ZoneId = designation.ZoneId, ArtifactId = designation.ArtifactId, MachineId = designation.MachineId,
            OccurredAtUtc = DateTime.UtcNow, Action = "revoked", Reason = reason, Generation = state.Generation
        });
    }
}
