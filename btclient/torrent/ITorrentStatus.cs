namespace btclient.torrent;

public interface ITorrentStatus
{
    public double PercentageComplete { get; }
    public TorrentDownloadState State { get; }
    
    public enum TorrentDownloadState { DOWNLOADING, DOWNLOADED, WAITING }
}