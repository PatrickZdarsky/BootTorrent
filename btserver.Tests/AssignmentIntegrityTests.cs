using boottorrent_lib.torrent;
using btserver.Swarm;

namespace btserver.Tests;

public class AssignmentIntegrityTests
{
    [Fact]
    public void AssignmentIncludesExpectedFileSha256()
    {
        var artifact = new TorrentArtifact
        {
            ID = "artifact", Name = "study", TorrentFileUrl = "https://example.invalid/torrent",
            IntegritySpec = new IntegritySpec { FileSha256 = new string('a', 64) }
        };

        var job = ZoneArtifactAssignmentService.CreateTorrentJob(artifact);

        Assert.Equal(artifact.IntegritySpec.FileSha256, job.FileSha256);
    }
}
