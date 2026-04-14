namespace boottorrent_lib.torrent;

public class TorrentArtifact
{
    public string InfoHashV1 => Torrent.InfoHashV1;
    public string InfoHashV2 => Torrent.InfoHashV2;
    public string Name { get; init; } = default!;
    public string ID { get; init; } = default!;
    
    
    public TorrentDescriptor Torrent { get; init; } = default!;
    public IntegritySpec IntegritySpec { get; init; } = default!;
}