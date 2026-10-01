using btserver.Swarm;
using btserver.Zone;

namespace btserver.Tests;

public class ZoneUnassignmentTests
{
    [Fact]
    public void OverlappingZoneAssignmentKeepsTheArtifactOnSharedMachine()
    {
        var shared = ProxyTestFixture.Machine("shared", "10.0.0.1");
        var affectedOnly = ProxyTestFixture.Machine("affected-only", "10.0.0.2");
        var removed = new StaticZone
        {
            Id = Guid.NewGuid(), Name = "removed", IsDistributionZone = false,
            MachineIds = [shared.Id, affectedOnly.Id]
        };
        var remaining = new StaticZone
        {
            Id = Guid.NewGuid(), Name = "remaining", IsDistributionZone = false,
            MachineIds = [shared.Id], AssignedArtifactIds = ["artifact"]
        };

        var recipients = ZoneArtifactAssignmentService.SelectUnassignmentRecipients(
            new HashSet<Guid> { removed.Id }, "artifact", [removed, remaining], false, [shared, affectedOnly]);

        Assert.Equal([affectedOnly.Id], recipients);
    }

    [Fact]
    public void DefaultZoneUnassignmentPreservesOverlappingManagementAssignment()
    {
        var shared = ProxyTestFixture.Machine("shared", "10.0.0.1");
        var defaultOnly = ProxyTestFixture.Machine("default-only", "10.0.0.2");
        var management = new StaticZone
        {
            Id = Guid.NewGuid(), Name = "management", IsDistributionZone = false,
            MachineIds = [shared.Id], AssignedArtifactIds = ["artifact"]
        };

        var recipients = ZoneArtifactAssignmentService.SelectUnassignmentRecipients(
            new HashSet<Guid> { ZoneManager.DefaultDistributionZoneId }, "artifact", [management], false,
            [shared, defaultOnly]);

        Assert.Equal([defaultOnly.Id], recipients);
    }
}
