using MonoTorrent.Client;

namespace btclient.torrent.monotorrent;

public class MonoTorrentStatus(TorrentManager torrentManager) : ITorrentStatus
{

    public double PercentageComplete => torrentManager.Progress;
    public ITorrentStatus.TorrentDownloadState State => torrentManager.State switch
    {
        TorrentState.Starting => ITorrentStatus.TorrentDownloadState.DOWNLOADING,
        TorrentState.Downloading => ITorrentStatus.TorrentDownloadState.DOWNLOADING,
        TorrentState.Seeding => ITorrentStatus.TorrentDownloadState.DOWNLOADED,
        _ => ITorrentStatus.TorrentDownloadState.WAITING
    };
}