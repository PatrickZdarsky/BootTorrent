namespace boottorrent_lib.torrent;

public class TorrentArtifact
{
    public string InfoHashV1 => torrent.InfoHashV1;
    public string InfoHashV2 => torrent.InfoHashV2;
    public string Name { get; init; } = default!;
    public string ID { get; init; } = default!;
    
    
    public TorrentDescriptor torrent;
    public IntegritySpec integritySpec;
}