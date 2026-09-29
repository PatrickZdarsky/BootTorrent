using boottorrent_lib.client;

namespace btserver.Swarm;

public interface IMachineRegistryObserver
{
    Task OnMachineRegisteredAsync(Machine machine);

    Task OnMachineHeartbeatAsync(Machine machine);
}
