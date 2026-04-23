using boottorrent_lib.torrent;

namespace btclient.torrent;

public interface ITorrentClient
{
    Task<ITorrentStatus> AddTorrentAsync(TorrentJob torrentJob, string torrentFilePath, string downloadPath);
    Task RemoveTorrentAsync(string infoHash);
    Task StartAsync();
    Task StopAsync();
    List<ITorrentStatus> GetActiveTorrents();
}