using System.Security.Cryptography;
using System.Text;

namespace boottorrent_lib.client;

/// <summary>
/// The configuration of a machine with the zones it belongs to, and any additional information that the server wants to send to the client.
/// </summary>
public class MachineConfiguration
{
    public string ConfigHash { get; set; } = string.Empty;

    public List<string> AssignedZones { get; set; } = [];

    public string? DistributionZoneId { get; set; }

    public List<string> ProxyArtifactIds { get; set; } = [];

    public string? OriginPeerAddress { get; set; }

    public int OriginPeerPort { get; set; }

    public static MachineConfiguration Create(
        IEnumerable<string>? assignedZones, string? distributionZoneId = null,
        IEnumerable<string>? proxyArtifactIds = null, string? originPeerAddress = null, int originPeerPort = 0)
    {
        var normalizedZones = NormalizeValues(assignedZones);
        var normalizedProxyArtifacts = NormalizeValues(proxyArtifactIds);
        var normalizedDistributionZoneId = string.IsNullOrWhiteSpace(distributionZoneId) ? null : distributionZoneId.Trim();
        var normalizedOriginAddress = string.IsNullOrWhiteSpace(originPeerAddress) ? null : originPeerAddress.Trim();
        return new MachineConfiguration
        {
            AssignedZones = normalizedZones,
            DistributionZoneId = normalizedDistributionZoneId,
            ProxyArtifactIds = normalizedProxyArtifacts,
            OriginPeerAddress = normalizedOriginAddress,
            OriginPeerPort = originPeerPort,
            ConfigHash = ComputeHash(normalizedZones, normalizedDistributionZoneId,
                normalizedProxyArtifacts, normalizedOriginAddress, originPeerPort)
        };
    }

    public void Normalize()
    {
        AssignedZones = NormalizeValues(AssignedZones);
        ProxyArtifactIds = NormalizeValues(ProxyArtifactIds);
        DistributionZoneId = string.IsNullOrWhiteSpace(DistributionZoneId) ? null : DistributionZoneId.Trim();
        OriginPeerAddress = string.IsNullOrWhiteSpace(OriginPeerAddress) ? null : OriginPeerAddress.Trim();
        ConfigHash = ComputeHash(AssignedZones, DistributionZoneId, ProxyArtifactIds, OriginPeerAddress, OriginPeerPort);
    }

    public static string ComputeHash(
        IEnumerable<string>? assignedZones, string? distributionZoneId = null,
        IEnumerable<string>? proxyArtifactIds = null, string? originPeerAddress = null, int originPeerPort = 0)
    {
        var normalizedZones = NormalizeValues(assignedZones);
        var normalizedProxyArtifacts = NormalizeValues(proxyArtifactIds);
        var payload = Encoding.UTF8.GetBytes(
            $"{distributionZoneId?.Trim() ?? string.Empty}\n{string.Join('\n', normalizedZones)}\n--proxies--\n" +
            $"{string.Join('\n', normalizedProxyArtifacts)}\n--origin--\n{originPeerAddress?.Trim() ?? string.Empty}:{originPeerPort}");
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    private static List<string> NormalizeValues(IEnumerable<string>? values)
    {
        return (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
    }
}
