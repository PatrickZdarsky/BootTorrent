using boottorrent_lib.torrent;

namespace btclient.torrent;

public class FailedTorrentStatus : ITorrentStatus
{
    public event EventHandler? StateChanged;
    public TorrentJob TorrentJob { get; init; }
    public double PercentageComplete => 0.0;
    public ITorrentStatus.TorrentDownloadState State => ITorrentStatus.TorrentDownloadState.FAILED;
}