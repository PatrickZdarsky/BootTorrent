using boottorrent_lib.artifact;

namespace boottorrent_lib.torrent;

public class TorrentArtifact : Artifact
{
    public string InfoHashV1 => Torrent.InfoHashV1;
    public string InfoHashV2 => Torrent.InfoHashV2;
    
    
    
    public TorrentDescriptor Torrent { get; init; } = default!;
    public IntegritySpec IntegritySpec { get; init; } = default!;
}