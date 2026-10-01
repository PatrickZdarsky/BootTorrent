using boottorrent_lib.client;

namespace btserver.Swarm;

/// <summary>Selects new proxy candidates from eligible machines.</summary>
public interface IProxySelectionPolicy
{
    string Key { get; }
    
    /// <summary>Selects up to the requested number of machines while excluding existing proxies.</summary>
    /// <param name="eligibleMachines">Machines eligible to serve as proxies.</param>
    /// <param name="incumbentMachineIds">IDs already designated as proxies.</param>
    /// <param name="preferredMachineIds">Machine IDs preferred by the zone configuration, in order.</param>
    /// <param name="count">Maximum number of new candidates to select.</param>
    /// <returns>The selected candidates in policy order.</returns>
    IReadOnlyList<Machine> Select(
        IReadOnlyCollection<Machine> eligibleMachines,
        IReadOnlyCollection<string> incumbentMachineIds,
        IReadOnlyList<string> preferredMachineIds,
        int count);
}

/// <summary>Orders preferred machines first, then selects by ordinal machine ID for a stable result.</summary>
public sealed class StableMachineIdProxySelectionPolicy : IProxySelectionPolicy
{
    /// <inheritdoc />
    public string Key => "stable-id";

    /// <inheritdoc />
    public IReadOnlyList<Machine> Select(
        IReadOnlyCollection<Machine> eligibleMachines,
        IReadOnlyCollection<string> incumbentMachineIds,
        IReadOnlyList<string> preferredMachineIds,
        int count)
    {
        var incumbents = incumbentMachineIds.ToHashSet(StringComparer.Ordinal);
        var preferenceRank = preferredMachineIds
            .Select((id, index) => (id, index))
            .GroupBy(entry => entry.id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);

        return eligibleMachines
            .Where(machine => !incumbents.Contains(machine.Id))
            .OrderBy(machine => preferenceRank.GetValueOrDefault(machine.Id, int.MaxValue))
            .ThenBy(machine => machine.Id, StringComparer.Ordinal)
            .Take(Math.Max(0, count))
            .ToList();
    }
}
