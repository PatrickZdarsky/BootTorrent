namespace boottorrent_lib.torrent;

public sealed class TorrentDescriptor
{
    public string InfoHash { get; init; } = default!; // hex/base32—your format
    public string Name { get; init; } = default!;
    public long SizeBytes { get; init; }              // total size
    public string TorrentUrl { get; init; } = default!;
    public string? TrackerUrl { get; init; }          // may be null if using peer injection
    public bool IsPrivate { get; init; }              // disable DHT/PEX/LSD when true
}