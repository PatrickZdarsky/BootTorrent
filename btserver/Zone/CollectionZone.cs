using System.Collections;
using boottorrent_lib.client;

namespace btserver.Zone;

/// <summary>
/// A simple zone which is just a List of machines
/// </summary>
public abstract class CollectionZone(string name, ICollection<Machine> machines) : IZone
{
    
    public IEnumerator<Machine> GetEnumerator()
    {
        return machines.GetEnumerator();
    }

    public string Name => name;

    public virtual bool Contains(Machine machine)
    {
        return machines.Contains(machine);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}