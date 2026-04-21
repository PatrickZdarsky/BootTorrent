namespace boottorrent_lib.torrent;

public class TorrentJob
{
    public string JobId => $"{ArtifactId}@{SavePath}";
    public string ArtifactId { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string TorrentFileUrl { get; set; } = default!;
    
    public string SavePath { get; init; } = null!;
    public DestinationSelector DestinationSelector { get; init; } = null!;
}