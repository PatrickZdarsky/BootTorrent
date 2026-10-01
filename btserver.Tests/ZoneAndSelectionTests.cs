using boottorrent_lib.client;
using btserver.Swarm;
using btserver.Zone;

namespace btserver.Tests;

public class ZoneAndSelectionTests
{
    [Fact]
    public void NoDistributionMatchUsesDefaultAndKeepsManagementMembership()
    {
        var machine = ProxyTestFixture.Machine("client", "10.0.0.1");
        var management = new StaticZone
        {
            Id = Guid.NewGuid(), Name = "management", IsDistributionZone = false,
            MachineIds = [machine.Id]
        };

        var membership = ZoneManager.Resolve(machine, [management]);

        Assert.Equal(ZoneManager.DefaultDistributionZoneId.ToString(), membership.DistributionZoneId);
        Assert.Contains(management.Id.ToString(), membership.ManagementZoneIds);
        Assert.Contains(ZoneManager.DefaultDistributionZoneId.ToString(), membership.ManagementZoneIds);
        Assert.False(membership.IsAmbiguous);
    }

    [Fact]
    public void OverlappingManagementZonesKeepOneDistributionBoundary()
    {
        var machine = ProxyTestFixture.Machine("client", "10.0.0.1");
        var zones = new[]
        {
            new StaticZone { Id = Guid.NewGuid(), Name = "distribution", MachineIds = [machine.Id] },
            new StaticZone { Id = Guid.NewGuid(), Name = "management-a", IsDistributionZone = false, MachineIds = [machine.Id] },
            new StaticZone { Id = Guid.NewGuid(), Name = "management-b", IsDistributionZone = false, MachineIds = [machine.Id] }
        };

        var membership = ZoneManager.Resolve(machine, zones);

        Assert.Equal(zones[0].Id.ToString(), membership.DistributionZoneId);
        Assert.Equal(3, membership.ManagementZoneIds.Count);
        Assert.False(membership.IsAmbiguous);
    }

    [Fact]
    public void OverlappingDistributionZonesHaveNoResolvedDistributionZone()
    {
        var machine = ProxyTestFixture.Machine("client", "10.0.0.1");
        var zones = new[]
        {
            new StaticZone { Id = Guid.NewGuid(), Name = "first", MachineIds = [machine.Id] },
            new StaticZone { Id = Guid.NewGuid(), Name = "second", MachineIds = [machine.Id] }
        };

        var membership = ZoneManager.Resolve(machine, zones);

        Assert.Null(membership.DistributionZoneId);
        Assert.True(membership.IsAmbiguous);
        Assert.Equal(zones.Select(zone => zone.Id.ToString()).Order(StringComparer.Ordinal),
            membership.ConflictingDistributionZoneIds);
    }

    [Fact]
    public void AddingDistributionZoneMovesExistingMachineOutOfDefault()
    {
        var machine = ProxyTestFixture.Machine("client", "10.0.0.1");
        Assert.Equal(ZoneManager.DefaultDistributionZoneId.ToString(), ZoneManager.Resolve(machine, []).DistributionZoneId);

        var zone = new StaticZone { Id = Guid.NewGuid(), Name = "new zone", MachineIds = [machine.Id] };
        Assert.Equal(zone.Id.ToString(), ZoneManager.Resolve(machine, [zone]).DistributionZoneId);
    }

    [Fact]
    public void StablePolicyPrefersConfiguredMachinesAndExcludesIncumbents()
    {
        var policy = new StableMachineIdProxySelectionPolicy();
        Machine[] machines =
        [
            ProxyTestFixture.Machine("z", "10.0.0.1"),
            ProxyTestFixture.Machine("b", "10.0.0.2"),
            ProxyTestFixture.Machine("a", "10.0.0.3"),
            ProxyTestFixture.Machine("c", "10.0.0.4")
        ];

        var selected = policy.Select(machines, ["b"], ["z", "c"], 3);

        Assert.Equal(["z", "c", "a"], selected.Select(machine => machine.Id));
    }
}
