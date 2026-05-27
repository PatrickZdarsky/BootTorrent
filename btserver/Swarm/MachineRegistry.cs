using System.Collections.Concurrent;
using boottorrent_lib.client;
using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using btserver.Config;
using Microsoft.Extensions.Options;

namespace btserver.Swarm;

public class MachineRegistry : IHostedService, IDisposable
{
    private readonly ILogger<MachineRegistry> _logger;
    private readonly TimeSpan _heartbeatTimeout;
    private readonly TimeSpan _heartbeatCheckInterval;
    private readonly CancellationTokenSource _monitorCancellationTokenSource = new();
    private Task? _monitorTask;

    public ConcurrentDictionary<string, Machine> Machines { get; } = new();

    public event EventHandler<MachineRegistryEventArgs>? MachineStarted;

    public event EventHandler<MachineRegistryEventArgs>? MachineStopped;

    public MachineRegistry(
        ILogger<MachineRegistry> logger,
        ServerMqttService mqttService,
        MachineConfigurationService machineConfigurationService,
        IOptions<MachineRegistryConfig> settings)
    {
        _logger = logger;
        _heartbeatTimeout = TimeSpan.FromSeconds(Math.Max(1, settings.Value.HeartbeatTimeoutSeconds));
        _heartbeatCheckInterval = TimeSpan.FromSeconds(Math.Max(1, settings.Value.HeartbeatCheckIntervalSeconds));
        RegisterHandlers(mqttService, machineConfigurationService);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _monitorTask = Task.Run(() => MonitorHeartbeatsAsync(_monitorCancellationTokenSource.Token), cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_monitorTask is null)
        {
            return;
        }

        await _monitorCancellationTokenSource.CancelAsync();

        try
        {
            await _monitorTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _monitorCancellationTokenSource.Dispose();
    }

    private void RegisterHandlers(ServerMqttService mqttService, MachineConfigurationService machineConfigurationService)
    {
        mqttService.AddHandler<MachineStartedMessage>(MachineStartedMessage.MessageType, async (context, message) =>
        {
            var machineId = context.TargetId!;
            var isNewMachine = false;
            var machine = Machines.AddOrUpdate(
                machineId,
                _ =>
                {
                    isNewMachine = true;
                    return CreateMachine(machineId, message);
                },
                (_, existingMachine) =>
                {
                    existingMachine.LastSeen = DateTime.UtcNow;
                    existingMachine.LoadedArtifacts = [];
                    existingMachine.PendingArtifacts = [];
                    existingMachine.ReportedConfigHash = string.Empty;
                    return existingMachine;
                });

            _logger.LogInformation("Machine {MachineId} started with IP address {IpAddress}.", machineId, machine.IpAddress);
            if (isNewMachine)
            {
                OnMachineStarted(machine);
            }

            await machineConfigurationService.EnsureConfigurationAsync(machine);
        });

        mqttService.AddHandler<MachineStoppedMessage>(MachineStoppedMessage.MessageType, (context, _) =>
        {
            if (TryRemoveMachine(context.TargetId!, MachineRegistryStopReason.StopMessage, out var _))
            {
                _logger.LogInformation("Machine {MachineId} stopped.", context.TargetId);
            }

            return Task.CompletedTask;
        });

        mqttService.AddHandler<MachineHeartbeatMessage>(MachineHeartbeatMessage.MessageType, async (context, message) =>
        {
            var machineId = context.TargetId!;
            if (!Machines.TryGetValue(machineId, out var machine))
            {
                _logger.LogWarning("Received a heartbeat for unknown machine {MachineId}. Requesting re-register.", machineId);
                await mqttService.PublishAsync(new MachineReRegisterMessage(), MqttTopicContext.CreateCommandForMachine(machineId, MachineReRegisterMessage.MessageType));
                return;
            }

            machine.LastSeen = DateTime.UtcNow;
            machine.LoadedArtifacts = message.LoadedArtifacts;
            machine.PendingArtifacts = message.PendingArtifacts;
            machine.ReportedConfigHash = message.ConfigHash;
            _logger.LogTrace("Received heartbeat for machine {MachineId}.", machineId);

            if (!string.Equals(machine.ReportedConfigHash, machine.DesiredConfigHash, StringComparison.Ordinal))
            {
                await machineConfigurationService.EnsureConfigurationAsync(machine);
            }
        });
    }

    private async Task MonitorHeartbeatsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_heartbeatCheckInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var timeoutThreshold = DateTime.UtcNow - _heartbeatTimeout;
                foreach (var machine in Machines.Values)
                {
                    if (machine.LastSeen >= timeoutThreshold)
                    {
                        continue;
                    }

                    if (TryRemoveMachine(machine.Id, MachineRegistryStopReason.HeartbeatTimeout, out var removedMachine))
                    {
                        _logger.LogWarning("Machine {MachineId} timed out after no heartbeat was received for {HeartbeatTimeout}.", removedMachine.Id, _heartbeatTimeout);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Machine CreateMachine(string machineId, MachineStartedMessage message)
    {
        return new Machine(machineId, message.IPAddress)
        {
            LastSeen = DateTime.UtcNow
        };
    }

    private bool TryRemoveMachine(string machineId, MachineRegistryStopReason stopReason, out Machine machine)
    {
        var removed = Machines.TryRemove(machineId, out var removedMachine);
        machine = removedMachine!;

        if (!removed || removedMachine is null)
        {
            return false;
        }

        OnMachineStopped(removedMachine, stopReason);
        return true;
    }

    private void OnMachineStarted(Machine machine)
    {
        MachineStarted?.Invoke(this, new MachineRegistryEventArgs(machine));
    }

    private void OnMachineStopped(Machine machine, MachineRegistryStopReason stopReason)
    {
        MachineStopped?.Invoke(this, new MachineRegistryEventArgs(machine, stopReason));
    }
}
