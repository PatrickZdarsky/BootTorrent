using boottorrent_lib.client;

namespace btserver.Swarm;

/// <summary>Provides machines currently known to the server as active.</summary>
public interface IActiveMachineSource
{
    /// <summary>Gets a snapshot of currently registered machines.</summary>
    /// <returns>The active machines at the time of the call.</returns>
    IReadOnlyCollection<Machine> GetActiveMachines();
}
