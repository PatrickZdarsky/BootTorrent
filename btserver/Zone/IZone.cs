using boottorrent_lib.client;

namespace btserver.Zone;

/// <summary>
/// A collection of multiple machines.
/// </summary>
public interface IZone
{
    string Name { get; }
    
    bool Contains(Machine machine);

    IEnumerable<Machine> Filter(IEnumerable<Machine> machines)
    {
        return machines.Where(Contains);
    }
}