using boottorrent_lib.torrent;

namespace btserver.torrent;

public class ITorrentTrackerHandler
{
    Task<List<TorrentArtifact>> GetAvailableTorrentsAsync(string clientId);
}