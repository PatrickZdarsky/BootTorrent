namespace btclient.torrent;

public interface ITorrentClient
{
    Task AddTorrentAsync(string torrentFilePath);
    Task RemoveTorrentAsync(string infoHash);
    Task StartAsync();
    Task StopAsync();
}