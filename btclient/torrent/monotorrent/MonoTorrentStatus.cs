using MonoTorrent.Client;

namespace btclient.torrent.monotorrent;

public class MonoTorrentStatus(TorrentManager torrentManager) : ITorrentStatus
{
    public event EventHandler<object>? StateChanged
    {
        add => torrentManager.TorrentStateChanged += value;
        remove => torrentManager.TorrentStateChanged -= value;
    }
    
    public double PercentageComplete => torrentManager.Progress;
    public ITorrentStatus.TorrentDownloadState State => torrentManager.State switch
    {
        TorrentState.Starting or TorrentState.Downloading => ITorrentStatus.TorrentDownloadState.DOWNLOADING,
        TorrentState.Seeding => ITorrentStatus.TorrentDownloadState.DOWNLOADED,
        _ => ITorrentStatus.TorrentDownloadState.WAITING
    };
}