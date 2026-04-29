using boottorrent_lib.client;

namespace btserver.Zone;

/// <summary>
/// A collection of multiple machines.
/// </summary>
public interface IZone : IEnumerable<Machine>
{
    string Name { get; }
    
    abstract bool Contains(Machine machine);
}