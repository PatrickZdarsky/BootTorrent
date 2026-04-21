namespace btclient.torrent;

public interface ITorrentStatus
{
    event EventHandler<object> StateChanged;
            
    double PercentageComplete { get; }
    TorrentDownloadState State { get; }
    
    enum TorrentDownloadState { DOWNLOADING, DOWNLOADED, WAITING }
}