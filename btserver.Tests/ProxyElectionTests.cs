using btserver.Data;
using btserver.Zone;
using Microsoft.EntityFrameworkCore;

namespace btserver.Tests;

public class ProxyElectionTests
{
    private const string ArtifactId = "study-artifact";

    [Theory]
    [InlineData(1, new[] { "r" })]
    [InlineData(2, new[] { "p1" })]
    [InlineData(3, new[] { "p1", "p2" })]
    public async Task Rq2_OneProxyFailureOnlyReplacesTheLastProxy(int initialCount, string[] expectedAfterFailure)
    {
        using var fixture = new ProxyTestFixture();
        var ids = new[] { "p0", "p1", "p2", "r", "o1", "o2", "o3", "o4", "o5", "o6" };
        var zone = fixture.AddZone("study zone", true, ids, ArtifactId);
        fixture.Machines.Active.AddRange(ids.Select((id, index) =>
            ProxyTestFixture.Machine(id, $"10.0.0.{index + 1}")));
        if (initialCount == 1)
            fixture.Machines.Active.Single(machine => machine.Id == "r").PendingArtifacts[ArtifactId] = 0.5;
        using (var db = fixture.OpenDb())
        {
            db.ProxyZoneConfigurations.Add(new ProxyZoneConfiguration
            {
                ZoneId = zone.Id, DesiredInitialCount = initialCount,
                PreferredMachineIds = initialCount == 1
                    ? ["p0", "r", "p1", "p2"]
                    : ["p0", "p1", "p2", "r"]
            });
            await db.SaveChangesAsync();
        }

        await fixture.Election.ReconcileAsync(zone.Id, ArtifactId);
        Assert.Equal(ids.Take(initialCount), (await fixture.Election.GetDesignationsAsync(zone.Id, ArtifactId))
            .Select(designation => designation.MachineId));

        fixture.Machines.Active.RemoveAll(machine => machine.Id == "p0");
        await fixture.Election.ReconcileAsync(zone.Id, ArtifactId);

        var remaining = await fixture.Election.GetDesignationsAsync(zone.Id, ArtifactId);
        Assert.Equal(expectedAfterFailure, remaining.Select(designation => designation.MachineId));
        var transitions = await fixture.Election.GetTransitionsAsync(zone.Id, ArtifactId);
        Assert.Contains(transitions, transition => transition.MachineId == "p0" &&
            transition.Action == "revoked" && transition.Reason == "machine-unavailable-or-zone-changed");
        if (initialCount == 1)
        {
            Assert.Contains(transitions, transition => transition.MachineId == "r" &&
                transition.Action == "designated" && transition.Reason == "recovery");
            Assert.Equal(0.5, fixture.Machines.Active.Single(machine => machine.Id == "r").PendingArtifacts[ArtifactId]);
        }
        else
            Assert.DoesNotContain(transitions, transition => transition.Reason == "recovery");

        await fixture.Election.ReconcileAsync(zone.Id, ArtifactId);
        Assert.Equal(expectedAfterFailure, (await fixture.Election.GetDesignationsAsync(zone.Id, ArtifactId))
            .Select(designation => designation.MachineId));
    }

    [Fact]
    public async Task Rq2_CompleteCopyPreventsReplacementAfterLastProxyFails()
    {
        using var fixture = new ProxyTestFixture();
        var zone = fixture.AddZone("study zone", true, ["p0", "ordinary"], ArtifactId);
        fixture.Machines.Active.Add(ProxyTestFixture.Machine("p0", "10.0.0.1"));
        var ordinary = ProxyTestFixture.Machine("ordinary", "10.0.0.2");
        fixture.Machines.Active.Add(ordinary);

        await fixture.Election.UpdateConfigurationAsync(zone.Id, 1, "stable-id", ["p0"]);

        await fixture.Election.ReconcileAsync(zone.Id, ArtifactId);
        Assert.Single(await fixture.Election.GetDesignationsAsync(zone.Id, ArtifactId));

        ordinary.LoadedArtifacts.Add(ArtifactId);
        fixture.Machines.Active.RemoveAll(machine => machine.Id == "p0");
        await fixture.Election.ReconcileAsync(zone.Id, ArtifactId);

        Assert.Empty(await fixture.Election.GetDesignationsAsync(zone.Id, ArtifactId));
    }

    [Fact]
    public async Task Rq2_NoAvailableReplacementLeavesOriginAccessUnassigned()
    {
        using var fixture = new ProxyTestFixture();
        var zone = fixture.AddZone("study zone", true, ["p0"], ArtifactId);
        fixture.Machines.Active.Add(ProxyTestFixture.Machine("p0", "10.0.0.1"));
        await fixture.Election.ReconcileAsync(zone.Id, ArtifactId);

        fixture.Machines.Active.Clear();
        await fixture.Election.ReconcileAsync(zone.Id, ArtifactId);

        Assert.Empty(await fixture.Election.GetDesignationsAsync(zone.Id, ArtifactId));
        Assert.DoesNotContain(await fixture.Election.GetTransitionsAsync(zone.Id, ArtifactId),
            transition => transition.Reason == "recovery");
    }

    [Fact]
    public async Task UnreportedMachinesDoNotStartAnElection()
    {
        using var fixture = new ProxyTestFixture();
        var zone = fixture.AddZone("study zone", true, ["p0"], ArtifactId);
        var machine = ProxyTestFixture.Machine("p0", "10.0.0.1", reported: false);
        fixture.Machines.Active.Add(machine);

        await fixture.Election.ReconcileAsync(zone.Id, ArtifactId);
        Assert.Empty(await fixture.Election.GetDesignationsAsync(zone.Id, ArtifactId));
        using (var db = fixture.OpenDb())
            Assert.False((await db.ProxyElectionStates.SingleAsync()).Initialized);

        machine.HasReportedStatus = true;
        await fixture.Election.ReconcileAsync(zone.Id, ArtifactId);
        Assert.Equal("p0", Assert.Single(await fixture.Election.GetDesignationsAsync(zone.Id, ArtifactId)).MachineId);
    }

    [Fact]
    public async Task ManagementAssignmentElectsWithinTheMembersDistributionZone()
    {
        using var fixture = new ProxyTestFixture();
        var distribution = fixture.AddZone("distribution", true, ["p0"]);
        fixture.AddZone("management", false, ["p0"], ArtifactId);
        fixture.Machines.Active.Add(ProxyTestFixture.Machine("p0", "10.0.0.1"));

        await fixture.Election.ReconcileAllAsync();

        Assert.Equal("p0", Assert.Single(await fixture.Election.GetDesignationsAsync(distribution.Id, ArtifactId)).MachineId);
        Assert.True(await fixture.Election.IsMachineAssignedAsync(fixture.Machines.Active[0], ArtifactId));
    }

    [Fact]
    public async Task DefaultZoneDesignationIsRevokedWhenMachineMovesIntoExplicitDistributionZone()
    {
        using var fixture = new ProxyTestFixture();
        fixture.AssignDefault(ArtifactId);
        var machine = ProxyTestFixture.Machine("p0", "10.0.0.1");
        fixture.Machines.Active.Add(machine);
        await fixture.Election.ReconcileAllAsync();
        Assert.Single(await fixture.Election.GetDesignationsAsync(ZoneManager.DefaultDistributionZoneId, ArtifactId));

        fixture.AddZone("new distribution", true, [machine.Id]);
        await fixture.Election.ReconcileAllAsync();

        Assert.Empty(await fixture.Election.GetDesignationsAsync(ZoneManager.DefaultDistributionZoneId, ArtifactId));
        Assert.False(await fixture.Election.IsMachineAssignedAsync(machine, ArtifactId));
    }

    [Fact]
    public async Task CountChangeAndUnassignmentUpdatePersistedDesignations()
    {
        using var fixture = new ProxyTestFixture();
        var zone = fixture.AddZone("study zone", true, ["a", "b", "c"], ArtifactId);
        fixture.Machines.Active.AddRange(new[] { "a", "b", "c" }
            .Select((id, index) => ProxyTestFixture.Machine(id, $"10.0.0.{index + 1}")));

        await fixture.Election.UpdateConfigurationAsync(zone.Id, 3, "stable-id", null);
        Assert.Equal(3, (await fixture.Election.GetDesignationsAsync(zone.Id, ArtifactId)).Count);
        await fixture.Election.UpdateConfigurationAsync(zone.Id, 1, "stable-id", null);
        Assert.Equal("a", Assert.Single(await fixture.Election.GetDesignationsAsync(zone.Id, ArtifactId)).MachineId);

        using (var db = fixture.OpenDb())
        {
            var stored = await db.Zones.SingleAsync(item => item.Id == zone.Id);
            stored.AssignedArtifactIds = [];
            await db.SaveChangesAsync();
        }
        await fixture.Election.ReconcileAllAsync();

        Assert.Empty(await fixture.Election.GetDesignationsAsync(zone.Id, ArtifactId));
        using var verify = fixture.OpenDb();
        Assert.Empty(await verify.ProxyElectionStates.ToListAsync());
    }

    [Fact]
    public async Task ManualDesignationRejectsOfflineOrUnassignedMachines()
    {
        using var fixture = new ProxyTestFixture();
        var zone = fixture.AddZone("study zone", true, ["p0", "ordinary"], ArtifactId);
        fixture.Machines.Active.Add(ProxyTestFixture.Machine("p0", "10.0.0.1"));
        fixture.Machines.Active.Add(ProxyTestFixture.Machine("ordinary", "10.0.0.2"));

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Election.SetDesignationsAsync(zone.Id, ArtifactId, ["offline"]));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Election.SetDesignationsAsync(zone.Id, ArtifactId, ["p0", "ordinary"]));
        await fixture.Election.SetDesignationsAsync(zone.Id, ArtifactId, ["ordinary"]);
        Assert.Equal("ordinary", Assert.Single(await fixture.Election.GetDesignationsAsync(zone.Id, ArtifactId)).MachineId);
    }

    [Fact]
    public async Task Rq2_OperatorCanPinTheSoleProxyReplacementWithoutClearingPartialProgress()
    {
        using var fixture = new ProxyTestFixture();
        var zone = fixture.AddZone("study zone", true, ["p0", "candidate", "r"], ArtifactId);
        var original = ProxyTestFixture.Machine("p0", "10.0.0.1");
        var candidate = ProxyTestFixture.Machine("candidate", "10.0.0.2");
        var replacement = ProxyTestFixture.Machine("r", "10.0.0.3");
        replacement.PendingArtifacts[ArtifactId] = 0.5;
        fixture.Machines.Active.AddRange([original, candidate, replacement]);
        await fixture.Election.UpdateConfigurationAsync(zone.Id, 1, "stable-id", ["p0", "candidate"]);

        fixture.Machines.Active.Remove(original);
        await fixture.Election.ReconcileAsync(zone.Id, ArtifactId);
        Assert.Equal("candidate", Assert.Single(await fixture.Election.GetDesignationsAsync(zone.Id, ArtifactId)).MachineId);

        await fixture.Election.SetDesignationsAsync(zone.Id, ArtifactId, [replacement.Id]);

        Assert.Equal("r", Assert.Single(await fixture.Election.GetDesignationsAsync(zone.Id, ArtifactId)).MachineId);
        Assert.Equal(0.5, replacement.PendingArtifacts[ArtifactId]);
        Assert.Contains(await fixture.Election.GetTransitionsAsync(zone.Id, ArtifactId), transition =>
            transition.MachineId == "r" && transition.Action == "designated" && transition.Reason == "operator");
    }

    [Fact]
    public async Task RestartKeepsEligibleIncumbentsAndRevokesOnlyOfflineOnes()
    {
        using var fixture = new ProxyTestFixture();
        var zone = fixture.AddZone("study zone", true, ["p0", "p1", "ordinary"], ArtifactId);
        fixture.Machines.Active.AddRange(new[] { "p0", "p1", "ordinary" }
            .Select((id, index) => ProxyTestFixture.Machine(id, $"10.0.0.{index + 1}")));
        await fixture.Election.UpdateConfigurationAsync(zone.Id, 2, "stable-id", ["p0", "p1"]);

        var afterRestart = fixture.CreateFreshElectionService();
        await afterRestart.ReconcileAllAsync();
        Assert.Equal(["p0", "p1"], (await afterRestart.GetDesignationsAsync(zone.Id, ArtifactId))
            .Select(designation => designation.MachineId));

        fixture.Machines.Active.RemoveAll(machine => machine.Id == "p0");
        await afterRestart.ReconcileAllAsync();
        Assert.Equal("p1", Assert.Single(await afterRestart.GetDesignationsAsync(zone.Id, ArtifactId)).MachineId);
    }
}
