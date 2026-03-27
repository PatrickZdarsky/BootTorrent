using boottorrent_lib.torrent;

namespace btserver.torrent;

public interface ITorrentAccessPolicy
{
    /// <summary>
    /// Main entry point for individual torrent clients to manage which torrents they can access directly from the server
    /// </summary>
    /// <param name="clientId">The id of the client which is requesting available torrents</param>
    /// <returns></returns>
    Task<List<TorrentArtifact>> GetAvailableTorrentsAsync(string clientId);

    /// <summary>
    /// Used to check if a client is allowed to access a specific torrent, this is used by the TorrentSeeder before allowing a client to download a torrent from the server
    /// </summary>
    /// <param name="clientId"></param>
    /// <param name="torrentName"></param>
    /// <returns></returns>
    Task<bool> CanAccess(string clientId, string torrentName);
}