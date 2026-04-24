using System.Net;

namespace boottorrent_lib.client;

/// <summary>
/// 
/// </summary>
/// <param name="subnet">The subnet in CIDR notation (e.g. 192.168.0.0/24)</param>
public class SubnetZone(ICollection<Machine> machines, string subnet) : CollectionZone(machines)
{
    private readonly IPNetwork _ipNetwork = IPNetwork.Parse(subnet);
    
    public new IEnumerator<Machine> GetEnumerator()
    {
        IEnumerable<Machine> ms = this;
        return ms.Where(m => _ipNetwork.Contains(IPAddress.Parse(m.IpAddress))).GetEnumerator();
    }

    public override bool Contains(Machine machine)
    {
        return base.Contains(machine) && _ipNetwork.Contains(IPAddress.Parse(machine.IpAddress));
    }
}