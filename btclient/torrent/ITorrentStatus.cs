using boottorrent_lib.torrent;

namespace btclient.torrent;

public interface ITorrentStatus
{
    event EventHandler? StateChanged;

    TorrentJob TorrentJob { get; }
    double PercentageComplete { get; }
    TorrentDownloadState State { get; }
    
    enum TorrentDownloadState { DOWNLOADING, DOWNLOADED, WAITING }
}