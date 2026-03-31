namespace boottorrent_lib.torrent;

public sealed class TorrentDescriptor
{
    public string InfoHashV1 { get; init; } = default!;
    public string InfoHashV2 { get; init; } = default!;
    
    public byte[] TorrentFileBytes { get; init; } = default!; // raw .torrent file bytes
}