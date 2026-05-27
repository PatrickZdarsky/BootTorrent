using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
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
    }

    public required string Subnet { get; set; }

    public override bool Contains(Machine machine)
    {
        if (!TryParseNetwork(out var network) || !IPAddress.TryParse(machine.IpAddress, out var machineAddress))
        {
            return false;
        }

        return network.Contains(machineAddress);
    }

    public bool Equals(SubnetZone? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && Subnet == other.Subnet;
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

    public bool TryParseNetwork([NotNullWhen(true)] out IPNetwork2? network)
    {
        try
        {
            network = IPNetwork2.Parse(Subnet);
            return true;
        }
        catch
        {
            network = null;
            return false;
        }
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
