namespace btclient.torrent;

public interface ITorrentClient
{
    Task<ITorrentStatus> AddTorrentAsync(string torrentFilePath, string downloadPath);
    Task RemoveTorrentAsync(string infoHash);
    Task StartAsync();
    Task StopAsync();
    List<ITorrentStatus> GetActiveTorrents();
}