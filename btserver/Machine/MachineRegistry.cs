namespace btserver.Machine;

public class MachineRegistry(ILogger<MachineRegistry> logger)
{
    public Dictionary<string, Machine> Machines { get; } = new Dictionary<string, Machine>();
    
    public void MachineStarted(string machineId, string ipAddress)
    {
        if (Machines.ContainsKey(machineId))
        {
            logger.LogWarning("Received a MachineStarted message for machine {MachineId} which is already registered. Ignoring.", machineId);
            return;
        }
        
        var machine = new Machine(machineId, ipAddress);
        Machines[machineId] = machine;
        logger.LogInformation("Machine {MachineId} started with IP address {IpAddress}.", machineId, ipAddress);
    }
    
    public void MachineStopped(string machineId)
    {
        Machines.Remove(machineId);
        logger.LogInformation("Machine {MachineId} stopped.", machineId);
    }
    
    public void MachineHeartbeatReceived(string machineId, string ipAddress)
    {
        if (!Machines.ContainsKey(machineId))
        {
            logger.LogWarning("Received a heartbeat for machine {MachineId} which is not registered. Registering new machine.", machineId);
            var machine = new Machine(machineId, ipAddress);
            Machines[machineId] = machine;
        }
        else
        {
            Machines[machineId].LastSeen = DateTime.UtcNow;
            logger.LogTrace("Received heartbeat for machine {MachineId}.", machineId);
        }
    }
}