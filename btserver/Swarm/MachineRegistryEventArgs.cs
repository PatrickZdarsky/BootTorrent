using boottorrent_lib.client;

namespace btserver.Swarm;

public sealed class MachineRegistryEventArgs : EventArgs
{
    public MachineRegistryEventArgs(Machine machine, MachineRegistryStopReason? stopReason = null)
    {
        Machine = machine;
        StopReason = stopReason;
    }

    public Machine Machine { get; }

    public MachineRegistryStopReason? StopReason { get; }
}

public enum MachineRegistryStopReason
{
    StopMessage,
    HeartbeatTimeout
}
