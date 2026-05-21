using boottorrent_lib.client;
using boottorrent_lib.communication;
using boottorrent_lib.communication.message;

namespace btserver.Swarm;

public class MachineRegistry
{
    private readonly ILogger<MachineRegistry> _logger;
    //Todo: Maybe move this to valkey
    public Dictionary<string, Machine> Machines { get; } = new();

    public MachineRegistry(ILogger<MachineRegistry> logger, ServerMqttService mqttService)
    {
        _logger = logger;
        RegisterHandlers(mqttService);
    }

    private void RegisterHandlers(ServerMqttService mqttService)
    {
        mqttService.AddHandler<MachineStartedMessage>(MachineStartedMessage.MessageType, (context, message) =>
        {
            var machineId = context.TargetId!;
            if (Machines.ContainsKey(machineId))
            {
                _logger.LogWarning("Received a MachineStarted message for machine {MachineId} which is already registered. Ignoring.", machineId);
                return Task.CompletedTask;
            }
        
            var machine = new Machine(machineId, message.IPAddress);
            Machines[machineId] = machine;
            _logger.LogInformation("Machine {MachineId} started with IP address {IpAddress}.", machineId, message.IPAddress);
            return Task.CompletedTask;
        });
        mqttService.AddHandler<MachineStoppedMessage>(MachineStoppedMessage.MessageType, (context, _) =>
        {
            Machines.Remove(context.TargetId!);
            _logger.LogInformation("Machine {MachineId} stopped.", context.TargetId);
            return Task.CompletedTask;
        });
        mqttService.AddHandler<MachineHeartbeatMessage>(MachineHeartbeatMessage.MessageType, async (context, message) =>
        {
            var machineId = context.TargetId!;
            if (!Machines.TryGetValue(machineId, out var machine))
            {
                _logger.LogWarning("Received a MachineStarted message for machine {MachineId} which is already registered. Requesting Reregister.", machineId);
                await mqttService.PublishAsync(new MachineReRegisterMessage(), MqttTopicContext.CreateCommandForMachine(machineId, MachineReRegisterMessage.MessageType));
            }
            else
            {
                machine.LastSeen = DateTime.UtcNow;
                machine.LoadedArtifacts = message.LoadedArtifacts;
                machine.PendingArtifacts = message.PendingArtifacts;
                _logger.LogTrace("Received heartbeat for machine {MachineId}.", machineId);
            }
        });
    }
}