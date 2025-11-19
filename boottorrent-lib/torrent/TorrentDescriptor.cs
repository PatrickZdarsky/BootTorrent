namespace boottorrent_lib.torrent;

public sealed class TorrentDescriptor
{
    public string InfoHash { get; init; } = default!; // hex/base32—your format
    public byte[] TorrentFileBytes { get; init; } = default!; // raw .torrent file bytes
}