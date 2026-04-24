using System.Collections;

namespace boottorrent_lib.client;

/// <summary>
/// A simple zone which is just a List of machines
/// </summary>
public class CollectionZone(ICollection<Machine> machines) : IZone
{
    
    public IEnumerator<Machine> GetEnumerator()
    {
        return machines.GetEnumerator();
    }

    public virtual bool Contains(Machine machine)
    {
        return machines.Contains(machine);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}