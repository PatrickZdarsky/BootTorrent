using boottorrent_lib.client;
using btserver.Data;
using btserver.Swarm;
using btserver.Zone;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace btserver.Tests;

internal sealed class ProxyTestFixture : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceProvider _services;

    public MutableMachineSource Machines { get; } = new();
    public RecordingAssignmentPublisher Assignments { get; } = new();
    public ProxyElectionService Election { get; }
    public ZoneManager Zones { get; }

    public ProxyTestFixture()
    {
        _connection.Open();
        _services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<BtDbContext>(options => options.UseSqlite(_connection))
            .AddSingleton<IActiveMachineSource>(Machines)
            .AddSingleton<IArtifactAssignmentPublisher>(Assignments)
            .AddSingleton<IProxySelectionPolicy, StableMachineIdProxySelectionPolicy>()
            .AddSingleton<ProxyElectionService>()
            .AddSingleton<ZoneManager>()
            .BuildServiceProvider();
        using var scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<BtDbContext>().Database.Migrate();
        Election = _services.GetRequiredService<ProxyElectionService>();
        Zones = _services.GetRequiredService<ZoneManager>();
    }

    public BtDbContext OpenDb() => new(new DbContextOptionsBuilder<BtDbContext>()
        .UseSqlite(_connection).Options);

    public MachineConfigurationService CreateConfigurationService() => new(
        NullLogger<MachineConfigurationService>.Instance, Zones,
        _services.GetRequiredService<IServiceScopeFactory>(),
        new Lazy<ServerMqttService>(() => throw new InvalidOperationException("Unexpected MQTT publication.")));

    public ProxyElectionService CreateFreshElectionService() => new(
        _services.GetRequiredService<IServiceScopeFactory>(), Machines,
        [new StableMachineIdProxySelectionPolicy()], NullLogger<ProxyElectionService>.Instance);

    public StaticZone AddZone(string name, bool distribution, string[] machineIds, params string[] artifactIds)
    {
        var zone = new StaticZone
        {
            Id = Guid.NewGuid(), Name = name, IsDistributionZone = distribution,
            MachineIds = machineIds.ToList(), AssignedArtifactIds = artifactIds.ToList()
        };
        using var db = OpenDb();
        db.Zones.Add(zone);
        db.SaveChanges();
        return zone;
    }

    public void AssignDefault(string artifactId)
    {
        using var db = OpenDb();
        db.DefaultZoneArtifactAssignments.Add(new DefaultZoneArtifactAssignment { ArtifactId = artifactId });
        db.SaveChanges();
    }

    public static Machine Machine(string id, string ip, bool reported = true, params string[] completed) =>
        new(id, ip) { HasReportedStatus = reported, LoadedArtifacts = completed.ToList() };

    public void Dispose()
    {
        _services.Dispose();
        _connection.Dispose();
    }
}

internal sealed class MutableMachineSource : IActiveMachineSource
{
    public List<Machine> Active { get; } = [];
    public IReadOnlyCollection<Machine> GetActiveMachines() => Active.ToList();
}

internal sealed class RecordingAssignmentPublisher : IArtifactAssignmentPublisher
{
    public List<(string MachineId, string ArtifactId)> Published { get; } = [];
    public List<(string MachineId, string ArtifactId)> Unassigned { get; } = [];

    public Task PublishAssignmentToMachineAsync(string machineId, string artifactId, CancellationToken cancellationToken)
    {
        Published.Add((machineId, artifactId));
        return Task.CompletedTask;
    }

    public Task PublishUnassignmentToMachineAsync(string machineId, string artifactId, CancellationToken cancellationToken)
    {
        Unassigned.Add((machineId, artifactId));
        return Task.CompletedTask;
    }
}
