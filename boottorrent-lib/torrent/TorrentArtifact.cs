namespace boottorrent_lib.torrent;

public class TorrentArtifact
{
    public string InfoHash => torrent.InfoHash;
    public string Name { get; init; } = default!;
    public string ID { get; init; } = default!;
    
    
    public TorrentDescriptor torrent;
    public IntegritySpec integritySpec;
}