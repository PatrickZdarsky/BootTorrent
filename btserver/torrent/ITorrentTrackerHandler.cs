using boottorrent_lib.torrent;

namespace btserver.torrent;

public interface ITorrentTrackerHandler
{
    /// <summary>
    /// Main entry point for individual torrent clients to manage which torrents they can access directly from the server
    /// </summary>
    /// <param name="clientId">The id of the client which is requesting available torrents</param>
    /// <returns></returns>
    Task<List<TorrentArtifact>> GetAvailableTorrentsAsync(string clientId);
}