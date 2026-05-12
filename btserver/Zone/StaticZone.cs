using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using boottorrent_lib.client;

namespace btserver.Zone;

public class StaticZone : Zone, IEquatable<StaticZone>
{
    public StaticZone()
    {
        
    }
    
    public StaticZone(List<string> machineIds)
    {
        MachineIds = machineIds;
    }
    
    [NotMapped]
    public List<string> MachineIds { get; set; } = [];
    
    public string MachineIdsJson
    {
        get => JsonSerializer.Serialize(MachineIds);
        set => MachineIds = JsonSerializer.Deserialize<List<string>>(value) ?? [];
    }
    
    public override bool Contains(Machine machine)
    {
        return MachineIds.Contains(machine.Id);
    }

    public bool Equals(StaticZone? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && MachineIds.SequenceEqual(other.MachineIds);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((StaticZone)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, MachineIds);
    }

    public static bool operator ==(StaticZone? left, StaticZone? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(StaticZone? left, StaticZone? right)
    {
        return !Equals(left, right);
    }
}