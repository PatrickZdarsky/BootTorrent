namespace boottorrent_lib.torrent;

public sealed class TorrentStats
{
    public string JobId { get; init; } = default!;
    public string ArtifactId { get; init; } = default!;
    
    public double ProgressPercent { get; init; }        // 0..100
    public long BytesDownloaded { get; init; }
    public long BytesUploaded { get; init; }
    public long DownloadRateBps { get; init; }
    public long UploadRateBps { get; init; }
    public int ConnectedPeers { get; init; }
}