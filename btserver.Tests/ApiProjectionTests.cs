using boottorrent_lib.client;
using boottorrent_lib.torrent;
using btserver.Controllers.Mapping;
using btserver.Data;

namespace btserver.Tests;

public class ApiProjectionTests
{
    [Fact]
    public void MachineProjectionDistinguishesHeartbeatFromAcknowledgement()
    {
        var machine = new Machine("m1", "10.0.0.1")
        {
            HasReportedStatus = true,
            DesiredConfigHash = "new",
            ReportedConfigHash = "old"
        };

        var dto = machine.ToDto();
        Assert.Equal(machine.LastSeen, dto.LastSeenUtc);
        Assert.True(dto.HasReportedStatus);
        Assert.False(dto.ConfigurationAcknowledged);

        machine.ReportedConfigHash = "new";
        Assert.True(machine.ToDto().ConfigurationAcknowledged);
    }

    [Fact]
    public void ArtifactProjectionIncludesExpectedHashAndSize()
    {
        var artifact = new TorrentArtifact
        {
            ID = "artifact",
            Name = "study",
            Torrent = new TorrentDescriptor(),
            IntegritySpec = new IntegritySpec { FileSha256 = "ABCDEF" }
        };

        var dto = artifact.ToDto([], 1234);
        Assert.Equal(1234, dto.SizeBytes);
        Assert.Equal("ABCDEF", dto.FileSha256);
    }

    [Fact]
    public async Task ProxyTransitionsCanBeReadBeyondOneHundredWithoutGaps()
    {
        using var fixture = new ProxyTestFixture();
        var zoneId = Guid.NewGuid();
        using (var db = fixture.OpenDb())
        {
            for (var i = 0; i < 125; i++)
                db.ProxyElectionTransitions.Add(new ProxyElectionTransition
                {
                    ZoneId = zoneId, ArtifactId = "artifact", MachineId = "m1",
                    Action = "selected", Reason = "test", Generation = i,
                    OccurredAtUtc = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        var first = await fixture.Election.GetTransitionsAsync(zoneId, "artifact", limit: 100);
        var second = await fixture.Election.GetTransitionsAsync(zoneId, "artifact", afterId: first[^1].Id, limit: 100);

        Assert.Equal(100, first.Count);
        Assert.Equal(25, second.Count);
        Assert.Equal(125, first.Concat(second).Select(item => item.Id).Distinct().Count());
        Assert.True(first[^1].Id < second[0].Id);
    }
}
