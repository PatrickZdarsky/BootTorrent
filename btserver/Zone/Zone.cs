using boottorrent_lib.client;

namespace btserver.Zone;

/// <summary>
/// A grouping of machines.
/// </summary>
public abstract class Zone
{
    public required string Name { get; init; }
    public List<string> AssignedArtifactIds { get; init; } = [];


    public abstract bool Contains(Machine machine);

    public IEnumerable<Machine> Filter(IEnumerable<Machine> machines)
    {
        return machines.Where(Contains);
    }
}