using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace btserver.Data;

public class ProxyZoneConfiguration
{
    [Key]
    public Guid ZoneId { get; set; }
    public int DesiredInitialCount { get; set; } = 1;
    public string SelectionPolicyKey { get; set; } = "stable-id";

    [NotMapped]
    public List<string> PreferredMachineIds { get; set; } = [];

    public string PreferredMachineIdsJson
    {
        get => JsonSerializer.Serialize(PreferredMachineIds);
        set => PreferredMachineIds = JsonSerializer.Deserialize<List<string>>(value) ?? [];
    }
}
