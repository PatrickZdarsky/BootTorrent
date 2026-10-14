using System.Net;
using btserver.Config;
using btserver.torrent.tracker;
using Microsoft.Extensions.Options;

namespace btserver.torrent.impl;

/// <summary>Converts registered central seeders into tracker peers.</summary>
public sealed class CentralSeederPeerProvider(
    ISeederRegistry seeders,
    IOptions<TorrentConfig> settings,
    ILogger<CentralSeederPeerProvider> logger)
{
    /// <summary>Gets distinct central seeder peers for a torrent, excluding the requester's endpoint.</summary>
    /// <remarks>Wildcard bind addresses are resolved from the configured tracker URL.</remarks>
    /// <param name="infoHash">The torrent info hash.</param>
    /// <param name="requester">The announcing peer.</param>
    /// <param name="cancellationToken">Cancels the operation before querying seeders.</param>
    /// <returns>Seeder peers with resolvable endpoints.</returns>
    public async Task<IReadOnlyList<Peer>> GetPeersAsync(string infoHash, Peer requester, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<Peer>();
        foreach (var seeder in await seeders.GetSeedersForTorrent(infoHash))
        {
            var endpoint = TryResolve(seeder.GetClientEndpoint());
            if (endpoint is null || endpoint.Equals(requester.EndPoint)) continue;
            result.Add(new Peer(seeder.getClientId(), endpoint, 0, 0, 0));
        }
        return result.DistinctBy(peer => (peer.EndPoint, peer.PeerId)).ToList();
    }

    /// <summary>Resolves the endpoint the tracker advertises for a central seeder.</summary>
    public IPEndPoint? GetAdvertisedEndpoint(IPEndPoint endpoint) => TryResolve(endpoint);

    private IPEndPoint? TryResolve(IPEndPoint endpoint)
    {
        if (!endpoint.Address.Equals(IPAddress.Any) && !endpoint.Address.Equals(IPAddress.IPv6Any)) return endpoint;
        if (!Uri.TryCreate(settings.Value.TrackerUrl, UriKind.Absolute, out var trackerUri)) return null;
        if (IPAddress.TryParse(trackerUri.Host, out var address)) return new IPEndPoint(address, endpoint.Port);
        try
        {
            address = Dns.GetHostAddresses(trackerUri.Host)
                .FirstOrDefault(candidate => candidate.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            return address is null ? null : new IPEndPoint(address, endpoint.Port);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not resolve central seeder host {Host}.", trackerUri.Host);
            return null;
        }
    }
}
