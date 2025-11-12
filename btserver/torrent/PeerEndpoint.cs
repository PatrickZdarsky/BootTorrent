namespace btserver.torrent;

public sealed class PeerEndpoint
{
    public string PeerId { get; init; } = default!;   // e.g., "C-101"
    public string Ip { get; init; } = default!;       // "10.1.2.3" or "::1"
    public int Port { get; init; }                    // 51413
    public string Zone { get; init; } = default!;     // e.g., "Z-A208" or "CENTRAL"
}