using System.Net;
using boottorrent_lib.client;

namespace btserver.Zone;

/// <summary>
/// 
/// </summary>
/// <param name="subnet">The subnet in CIDR notation (e.g. 192.168.0.0/24)</param>
public class SubnetZone(string subnet) : Zone, IEquatable<SubnetZone>
{
    private readonly IPNetwork _ipNetwork = IPNetwork.Parse(subnet);
    

    public override bool Contains(Machine machine)
    {
        return _ipNetwork.Contains(IPAddress.Parse(machine.IpAddress));
    }

    public bool Equals(SubnetZone? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && _ipNetwork == other._ipNetwork;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((SubnetZone)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, subnet);
    }

    public static bool operator ==(SubnetZone? left, SubnetZone? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(SubnetZone? left, SubnetZone? right)
    {
        return !Equals(left, right);
    }
}