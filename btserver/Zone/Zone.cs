using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using boottorrent_lib.client;

namespace btserver.Zone;

/// <summary>
/// A grouping of machines.
/// </summary>
public abstract class Zone
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; }
    public required string Name { get; set; }
    
    [NotMapped]
    public List<string> AssignedArtifactIds { get; set; } = [];

    public string AssignedArtifactIdsJson
    {
        get => JsonSerializer.Serialize(AssignedArtifactIds);
        set => AssignedArtifactIds = JsonSerializer.Deserialize<List<string>>(value) ?? [];
    }


    public abstract bool Contains(Machine machine);

    public IEnumerable<Machine> Filter(IEnumerable<Machine> machines)
    {
        return machines.Where(Contains);
    }
}