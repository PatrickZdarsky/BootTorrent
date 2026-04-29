using boottorrent_lib.client;

namespace btserver.Zone;

public class StaticZone(string name, List<string> machineIds) : IZone, IEquatable<StaticZone>
{
    public string Name => name;
    private List<string> MachineIds => machineIds;
    
    public virtual bool Contains(Machine machine)
    {
        return machineIds.Contains(machine.Id);
    }

    public bool Equals(StaticZone? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && machineIds.SequenceEqual(other.MachineIds);
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
        return HashCode.Combine(Name, machineIds);
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