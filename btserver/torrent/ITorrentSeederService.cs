using boottorrent_lib.torrent;

namespace btserver.torrent;

public interface ITorrentSeederService : IHostedService
{
    Task EnsureSeedingAsync(string artifactId, CancellationToken cancellationToken = default);
}