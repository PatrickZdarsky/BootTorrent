using boottorrent_lib.client;
using btserver.Zone;

namespace btserver.Tests;

public class MachineConfigurationTests
{
    [Fact]
    public async Task LateClientReplaysOnlyMissingAssignmentsAfterConfigurationAcknowledgement()
    {
        using var fixture = new ProxyTestFixture();
        var management = fixture.AddZone("management", false, ["late"], "already-loaded", "pending");
        fixture.AssignDefault("missing");
        var defaultId = ZoneManager.DefaultDistributionZoneId.ToString();
        var hash = MachineConfiguration.Create([management.Id.ToString(), defaultId], defaultId).ConfigHash;
        var machine = ProxyTestFixture.Machine("late", "10.0.0.1", completed: ["already-loaded"]);
        machine.PendingArtifacts["pending"] = 0.5;
        machine.DistributionZoneId = defaultId;
        machine.DesiredConfigHash = hash;
        machine.ReportedConfigHash = hash;
        var service = fixture.CreateConfigurationService();

        await service.OnMachineHeartbeatAsync(machine);
        await service.OnMachineHeartbeatAsync(machine);

        Assert.Equal(("late", "missing"), Assert.Single(fixture.Assignments.Published));
        Assert.Equal(hash, machine.LastAssignmentReconciliationHash);
    }

    [Fact]
    public async Task ReturningClientRemovesArtifactNoLongerAssignedToAnyZone()
    {
        using var fixture = new ProxyTestFixture();
        var defaultId = ZoneManager.DefaultDistributionZoneId.ToString();
        var hash = MachineConfiguration.Create([defaultId], defaultId).ConfigHash;
        var machine = ProxyTestFixture.Machine("late", "10.0.0.1", completed: ["stale"]);
        machine.KnownArtifacts.Add("stale");
        machine.DistributionZoneId = defaultId;
        machine.DesiredConfigHash = hash;
        machine.ReportedConfigHash = hash;

        await fixture.CreateConfigurationService().OnMachineHeartbeatAsync(machine);

        Assert.Equal(("late", "stale"), Assert.Single(fixture.Assignments.Unassigned));
    }

    [Fact]
    public async Task PeriodicReconciliationRetriesMissingAssignments()
    {
        using var fixture = new ProxyTestFixture();
        fixture.AssignDefault("missing");
        var defaultId = ZoneManager.DefaultDistributionZoneId.ToString();
        var hash = MachineConfiguration.Create([defaultId], defaultId).ConfigHash;
        var machine = ProxyTestFixture.Machine("late", "10.0.0.1");
        machine.DistributionZoneId = defaultId;
        machine.DesiredConfigHash = hash;
        machine.ReportedConfigHash = hash;
        var service = fixture.CreateConfigurationService();

        await service.OnMachineHeartbeatAsync(machine);
        machine.LastAssignmentReconciliationUtc = DateTime.UtcNow.AddSeconds(-6);
        await service.OnMachineHeartbeatAsync(machine);

        Assert.Equal(2, fixture.Assignments.Published.Count);
    }
}
