using boottorrent_lib.torrent;

namespace btserver.torrent.impl;

public class TorrentAccessPolicy(ITorrentArtifactRegistry registry) : ITorrentAccessPolicy
{
    public async Task<List<TorrentArtifact>> GetAvailableTorrentsAsync(string clientId)
    {
        return (await registry.GetRegisteredArtifacts()).Values.ToList();
    }

    public Task<bool> CanAccessInfoHash(string clientId, string torrentInfoHash)
    {
        return Task.FromResult(true);
    }
}