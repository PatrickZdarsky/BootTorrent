using System.Net;
using boottorrent_lib.client;

namespace btclient.torrent.monotorrent;

/// <summary>Rejects the central seeder endpoint unless this machine currently has a proxy designation.</summary>
public sealed class OriginPeerConnectionPolicy
{
    private sealed record Snapshot(IPAddress? Address, int Port, bool IsProxy);

    private Snapshot _snapshot = new(null, 0, false);

    /// <summary>Applies the latest server configuration and reports whether existing connections must be reset.</summary>
    public bool Apply(MachineConfiguration configuration)
    {
        var address = IPAddress.TryParse(configuration.OriginPeerAddress, out var parsed) ? parsed : null;
        var next = new Snapshot(address, configuration.OriginPeerPort, configuration.ProxyArtifactIds.Count > 0);
        var previous = Interlocked.Exchange(ref _snapshot, next);
        return previous.IsProxy && (!next.IsProxy || !Equals(previous.Address, next.Address) || previous.Port != next.Port);
    }

    /// <summary>Checks one outbound peer before MonoTorrent opens the connection.</summary>
    public bool ShouldReject(Uri peerUri)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        return !snapshot.IsProxy && snapshot.Address is not null &&
               peerUri.Port == snapshot.Port &&
               IPAddress.TryParse(peerUri.Host, out var peerAddress) &&
               peerAddress.Equals(snapshot.Address);
    }
}
