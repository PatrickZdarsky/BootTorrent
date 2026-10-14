using boottorrent_lib.torrent;

namespace btclient.torrent;

public class FailedTorrentStatus : ITorrentStatus
{
    public event EventHandler? StateChanged;
    public TorrentJob TorrentJob { get; init; }
    public string? DownloadedFilePath => null;
    public string? InfoHash => null;
    public double PercentageComplete => 0.0;
    public ITorrentStatus.TorrentDownloadState State => ITorrentStatus.TorrentDownloadState.FAILED;
}
