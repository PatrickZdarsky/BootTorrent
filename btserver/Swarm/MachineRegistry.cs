using boottorrent_lib.client;
using boottorrent_lib.communication.message;

namespace btserver.Swarm;

public class MachineRegistry(ILogger<MachineRegistry> logger)
{
    //Todo: Maybe move this to valkey
    public Dictionary<string, Machine> Machines { get; } = new();


    public void RegisterHandlers(ServerMqttService mqttService)
    {
        mqttService.AddHandler<MachineStartedMessage>(MachineStartedMessage.MessageType,  (context, message) =>
        {
            var machineId = context.TargetId!;
            if (Machines.ContainsKey(machineId))
            {
                logger.LogWarning("Received a MachineStarted message for machine {MachineId} which is already registered. Ignoring.", machineId);
                return Task.CompletedTask;
            }
        
            var machine = new Machine(machineId, message.IPAddress);
            Machines[machineId] = machine;
            logger.LogInformation("Machine {MachineId} started with IP address {IpAddress}.", machineId, message.IPAddress);
            return Task.CompletedTask;
        });
        mqttService.AddHandler<MachineStoppedMessage>(MachineStoppedMessage.MessageType, (context, _) =>
        {
            Machines.Remove(context.TargetId!);
            logger.LogInformation("Machine {MachineId} stopped.", context.TargetId);
            return Task.CompletedTask;
        });
        mqttService.AddHandler<MachineHeartbeatMessage>(MachineHeartbeatMessage.MessageType, (context, message) =>
        {
            var machineId = context.TargetId!;
            if (!Machines.TryGetValue(machineId, out var machine))
            {
                logger.LogWarning("Received a heartbeat for machine {MachineId} which is not registered. Ignoring.", machineId);
                //Todo: Send command to machine to register itself again
            }
            else
            {
                machine.LastSeen = DateTime.UtcNow;
                logger.LogTrace("Received heartbeat for machine {MachineId}.", machineId);
            }
            return Task.CompletedTask;
        });
    }
}