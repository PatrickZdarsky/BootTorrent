using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace btserver.Data;

public class SubnetZonePolicyConfiguration
{
    [Key]
    public Guid ZoneId { get; set; }

    public int ProxyCount { get; set; } = 1;

    [NotMapped]
    public List<string> ProxyMachineIds { get; set; } = [];

    public string ProxyMachineIdsJson
    {
        get => JsonSerializer.Serialize(ProxyMachineIds);
        set => ProxyMachineIds = JsonSerializer.Deserialize<List<string>>(value) ?? [];
    }
}
