using boottorrent_lib.torrent;
using MonoTorrent.Client;

namespace btclient.torrent.monotorrent;

public class MonoTorrentStatus(TorrentManager torrentManager) : ITorrentStatus
{
    public event EventHandler? StateChanged
    {
        add => torrentManager.TorrentStateChanged += (sender, args) => value?.Invoke(sender, args);
        remove => torrentManager.TorrentStateChanged -= (sender, args) => value?.Invoke(sender, args);
    }

    public TorrentJob TorrentJob { get; init; }
    public string? DownloadedFilePath => torrentManager.Files.Count == 1 ? torrentManager.Files[0].FullPath : null;
    public string? InfoHash => torrentManager.InfoHashes.V1OrV2.ToHex();
    public double PercentageComplete => torrentManager.Progress;
    public ITorrentStatus.TorrentDownloadState State => torrentManager.State switch
    {
        TorrentState.Starting or TorrentState.Downloading => ITorrentStatus.TorrentDownloadState.DOWNLOADING,
        TorrentState.Seeding => ITorrentStatus.TorrentDownloadState.DOWNLOADED,
        _ => ITorrentStatus.TorrentDownloadState.WAITING
    };
}
