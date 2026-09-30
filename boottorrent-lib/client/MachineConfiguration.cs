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

    public static MachineConfiguration Create(IEnumerable<string>? assignedZones, string? distributionZoneId = null)
    {
        var normalizedZones = NormalizeZones(assignedZones);
        var normalizedDistributionZoneId = string.IsNullOrWhiteSpace(distributionZoneId) ? null : distributionZoneId.Trim();
        return new MachineConfiguration
        {
            AssignedZones = normalizedZones,
            DistributionZoneId = normalizedDistributionZoneId,
            ConfigHash = ComputeHash(normalizedZones, normalizedDistributionZoneId)
        };
    }

    public void Normalize()
    {
        AssignedZones = NormalizeZones(AssignedZones);
        DistributionZoneId = string.IsNullOrWhiteSpace(DistributionZoneId) ? null : DistributionZoneId.Trim();
        ConfigHash = ComputeHash(AssignedZones, DistributionZoneId);
    }

    public static string ComputeHash(IEnumerable<string>? assignedZones, string? distributionZoneId = null)
    {
        var normalizedZones = NormalizeZones(assignedZones);
        var payload = Encoding.UTF8.GetBytes($"{distributionZoneId?.Trim() ?? string.Empty}\n{string.Join('\n', normalizedZones)}");
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    private static List<string> NormalizeZones(IEnumerable<string>? assignedZones)
    {
        return (assignedZones ?? [])
            .Where(zoneId => !string.IsNullOrWhiteSpace(zoneId))
            .Select(zoneId => zoneId.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(zoneId => zoneId, StringComparer.Ordinal)
            .ToList();
    }
}
