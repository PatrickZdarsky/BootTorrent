namespace boottorrent_lib.torrent;

public class TorrentArtifact
{
    public string InfoHash => torrent.InfoHash;
    public string Name;
    
    
    public TorrentDescriptor torrent;
    public IntegritySpec integritySpec;
}