using System.Threading.Channels;

namespace btserver.Swarm;

/// <summary>Reconciles proxy elections at startup, after machine changes, and every 30 seconds.</summary>
public sealed class ProxyElectionCoordinator : BackgroundService
{
    private readonly MachineRegistry _registry;
    private readonly ProxyElectionService _election;
    private readonly ILogger<ProxyElectionCoordinator> _logger;
    private readonly Channel<bool> _signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });

    /// <summary>Subscribes to registry changes and prepares the reconciliation worker.</summary>
    /// <param name="registry">Source of machine state and stop events.</param>
    /// <param name="election">Service that reconciles proxy designations.</param>
    /// <param name="logger">Logger for reconciliation failures.</param>
    public ProxyElectionCoordinator(MachineRegistry registry, ProxyElectionService election, ILogger<ProxyElectionCoordinator> logger)
    {
        _registry = registry;
        _election = election;
        _logger = logger;
        _registry.MachineStateChanged += OnChanged;
        _registry.MachineStopped += OnChanged;
    }

    /// <summary>Processes coalesced reconciliation signals until the service stops.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _signals.Writer.TryWrite(true);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        var periodic = Task.Run(async () =>
        {
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken)) _signals.Writer.TryWrite(true);
            }
            catch (OperationCanceledException) { }
        }, stoppingToken);

        try
        {
            await foreach (var _ in _signals.Reader.ReadAllAsync(stoppingToken))
            {
                try { await _election.ReconcileAllAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { _logger.LogError(ex, "Proxy election reconciliation failed."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            _registry.MachineStateChanged -= OnChanged;
            _registry.MachineStopped -= OnChanged;
            await periodic;
        }
    }

    private void OnChanged(object? sender, MachineRegistryEventArgs args) => _signals.Writer.TryWrite(true);
}
