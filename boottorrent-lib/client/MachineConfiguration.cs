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

    public static MachineConfiguration Create(IEnumerable<string>? assignedZones)
    {
        var normalizedZones = NormalizeZones(assignedZones);
        return new MachineConfiguration
        {
            AssignedZones = normalizedZones,
            ConfigHash = ComputeHash(normalizedZones)
        };
    }

    public void Normalize()
    {
        AssignedZones = NormalizeZones(AssignedZones);
        ConfigHash = ComputeHash(AssignedZones);
    }

    public static string ComputeHash(IEnumerable<string>? assignedZones)
    {
        var normalizedZones = NormalizeZones(assignedZones);
        var payload = Encoding.UTF8.GetBytes(string.Join('\n', normalizedZones));
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
