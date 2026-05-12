using System.ComponentModel.DataAnnotations.Schema;
using System.Net;
using boottorrent_lib.client;

namespace btserver.Zone;

/// <summary>
/// 
/// </summary>
public class SubnetZone : Zone, IEquatable<SubnetZone>
{
    public SubnetZone()
    {
        
    }
    
    public SubnetZone(string subnet)
    {
        Subnet = subnet;
        _ipNetwork = IPNetwork.Parse(subnet);
    }
    
    [NotMapped]
    private readonly IPNetwork? _ipNetwork;
    
    public required string Subnet { get; set; }


    public override bool Contains(Machine machine)
    {
        if (_ipNetwork == null)
        {
            return false;
        }
        return _ipNetwork?.Contains(IPAddress.Parse(machine.IpAddress)) ?? false;
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
        return HashCode.Combine(Name, Subnet);
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