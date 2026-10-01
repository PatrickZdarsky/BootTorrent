using System.Net;
using boottorrent_lib.client;
using boottorrent_lib.torrent;
using btserver.Config;
using btserver.Swarm;
using btserver.torrent;
using btserver.torrent.impl;
using btserver.torrent.tracker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace btserver.Tests;

public class ZoneTorrentAccessPolicyTests
{
    private const string ArtifactId = "study-artifact";
    private const string InfoHash = "abc123";
    private static readonly IPEndPoint OriginEndpoint = new(IPAddress.Parse("10.9.0.1"), 55123);

    [Fact]
    public async Task Rq1_OrdinaryClientsDiscoverProxyAndOrdinaryPeersButNeverOrigin()
    {
        using var fixture = new ProxyTestFixture();
        var machines = AddStudyMachines(fixture);
        var zone = fixture.AddZone("study zone", true, machines.Select(machine => machine.Id).ToArray(), ArtifactId);
        await fixture.Election.UpdateConfigurationAsync(zone.Id, 1, "stable-id", ["p0"]);
        var policy = CreatePolicy(fixture);
        var available = machines.Select(PeerFor).ToList();

        var ordinaryPeers = await policy.GetPeersForAnnounceAsync(Request(machines[1], machines, available));
        var proxyPeers = await policy.GetPeersForAnnounceAsync(Request(machines[0], machines, available));

        Assert.Equal(["o2", "p0"], ordinaryPeers.Select(peer => peer.PeerId).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(ordinaryPeers, peer => peer.EndPoint.Equals(OriginEndpoint));
        Assert.Contains(proxyPeers, peer => peer.EndPoint.Equals(OriginEndpoint));
        Assert.Contains(proxyPeers, peer => peer.PeerId == "o1");
        Assert.Contains(proxyPeers, peer => peer.PeerId == "o2");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Rq3_OnlyConfiguredProxyCountCanDiscoverOrigin(int count)
    {
        using var fixture = new ProxyTestFixture();
        var ids = new[] { "p0", "p1", "p2", "ordinary", "o1", "o2", "o3", "o4", "o5", "o6" };
        var machines = ids.Select((id, index) => ProxyTestFixture.Machine(id, $"10.0.0.{index + 1}")).ToList();
        fixture.Machines.Active.AddRange(machines);
        var zone = fixture.AddZone("study zone", true, ids, ArtifactId);
        await fixture.Election.UpdateConfigurationAsync(zone.Id, count, "stable-id", ["p0", "p1", "p2"]);
        var policy = CreatePolicy(fixture);
        var announced = machines.Select(PeerFor).ToList();

        var designations = await fixture.Election.GetDesignationsAsync(zone.Id, ArtifactId);
        Assert.Equal(count, designations.Count);
        foreach (var machine in machines)
        {
            var peers = await policy.GetPeersForAnnounceAsync(Request(machine, machines, announced));
            Assert.Equal(designations.Any(item => item.MachineId == machine.Id),
                peers.Any(peer => peer.EndPoint.Equals(OriginEndpoint)));
        }
    }

    [Fact]
    public async Task Rq2_ReplacementGetsOriginButOtherOrdinaryClientsDoNot()
    {
        using var fixture = new ProxyTestFixture();
        var ids = new[] { "p0", "r", "ordinary" };
        var machines = ids.Select((id, index) => ProxyTestFixture.Machine(id, $"10.0.0.{index + 1}")).ToList();
        fixture.Machines.Active.AddRange(machines);
        var zone = fixture.AddZone("study zone", true, ids, ArtifactId);
        await fixture.Election.UpdateConfigurationAsync(zone.Id, 1, "stable-id", ["p0", "r"]);
        fixture.Machines.Active.RemoveAll(machine => machine.Id == "p0");
        await fixture.Election.ReconcileAsync(zone.Id, ArtifactId);
        var policy = CreatePolicy(fixture);
        var active = machines.Skip(1).ToList();
        var announced = active.Select(PeerFor).ToList();

        Assert.Equal("r", Assert.Single(await fixture.Election.GetDesignationsAsync(zone.Id, ArtifactId)).MachineId);
        Assert.Contains(await policy.GetPeersForAnnounceAsync(Request(active[0], active, announced)),
            peer => peer.EndPoint.Equals(OriginEndpoint));
        Assert.DoesNotContain(await policy.GetPeersForAnnounceAsync(Request(active[1], active, announced)),
            peer => peer.EndPoint.Equals(OriginEndpoint));
    }

    [Fact]
    public async Task LateClientSeesCompleteLocalPeerWithoutReceivingOrigin()
    {
        using var fixture = new ProxyTestFixture();
        var complete = ProxyTestFixture.Machine("complete", "10.0.0.1", completed: [ArtifactId]);
        var late = ProxyTestFixture.Machine("late", "10.0.0.2");
        fixture.Machines.Active.AddRange([complete, late]);
        var zone = fixture.AddZone("study zone", true, [complete.Id, late.Id], ArtifactId);
        await fixture.Election.ReconcileAsync(zone.Id, ArtifactId);
        var policy = CreatePolicy(fixture);

        var peers = await policy.GetPeersForAnnounceAsync(Request(late, [complete, late], [PeerFor(complete), PeerFor(late)]));

        Assert.Empty(await fixture.Election.GetDesignationsAsync(zone.Id, ArtifactId));
        Assert.Equal("complete", Assert.Single(peers).PeerId);
    }

    [Fact]
    public async Task SeparateDistributionZonesDoNotShareTrackerPeers()
    {
        using var fixture = new ProxyTestFixture();
        var first = ProxyTestFixture.Machine("first", "10.0.0.1");
        var local = ProxyTestFixture.Machine("local", "10.0.0.2");
        var remote = ProxyTestFixture.Machine("remote", "10.0.1.1");
        fixture.Machines.Active.AddRange([first, local, remote]);
        fixture.AddZone("first zone", true, [first.Id, local.Id], ArtifactId);
        fixture.AddZone("second zone", true, [remote.Id], ArtifactId);
        var policy = CreatePolicy(fixture);

        var peers = await policy.GetPeersForAnnounceAsync(Request(first, [first, local, remote],
            [PeerFor(first), PeerFor(local), PeerFor(remote)]));

        Assert.Contains(peers, peer => peer.PeerId == local.Id);
        Assert.DoesNotContain(peers, peer => peer.PeerId == remote.Id);
    }

    [Fact]
    public async Task UnknownAmbiguousAndUnassignedClientsReceiveNoPeers()
    {
        using var fixture = new ProxyTestFixture();
        var known = ProxyTestFixture.Machine("known", "10.0.0.1");
        var unassigned = ProxyTestFixture.Machine("unassigned", "10.0.0.2");
        fixture.Machines.Active.AddRange([known, unassigned]);
        fixture.AddZone("first zone", true, [known.Id, unassigned.Id], ArtifactId);
        var policy = CreatePolicy(fixture);
        Assert.Empty(await policy.GetPeersForAnnounceAsync(Request(unassigned, [known, unassigned], [PeerFor(known)])
            with { RequestingMachine = null }));

        fixture.AddZone("second zone", true, [known.Id]);
        Assert.Empty(await policy.GetPeersForAnnounceAsync(Request(known, [known, unassigned], [PeerFor(unassigned)])));

        var outside = ProxyTestFixture.Machine("outside", "10.0.2.1");
        Assert.Empty(await policy.GetPeersForAnnounceAsync(Request(outside, [known, unassigned, outside], [PeerFor(known)])));
    }

    [Fact]
    public async Task ProxyReservesPeerSlotForOrigin()
    {
        using var fixture = new ProxyTestFixture();
        var machines = AddStudyMachines(fixture);
        var zone = fixture.AddZone("study zone", true, machines.Select(machine => machine.Id).ToArray(), ArtifactId);
        await fixture.Election.UpdateConfigurationAsync(zone.Id, 1, "stable-id", ["p0"]);
        var peers = await CreatePolicy(fixture).GetPeersForAnnounceAsync(
            Request(machines[0], machines, machines.Select(PeerFor).ToList()) with { MaxPeers = 1 });

        Assert.Equal(OriginEndpoint, Assert.Single(peers).EndPoint);
    }

    [Fact]
    public async Task OrdinaryClientWithNoLocalPeerNeverFallsBackToOrigin()
    {
        using var fixture = new ProxyTestFixture();
        var proxy = ProxyTestFixture.Machine("p0", "10.0.0.1");
        var ordinary = ProxyTestFixture.Machine("ordinary", "10.0.0.2");
        fixture.Machines.Active.AddRange([proxy, ordinary]);
        var zone = fixture.AddZone("study zone", true, [proxy.Id, ordinary.Id], ArtifactId);
        await fixture.Election.UpdateConfigurationAsync(zone.Id, 1, "stable-id", [proxy.Id]);

        var peers = await CreatePolicy(fixture).GetPeersForAnnounceAsync(
            Request(ordinary, [proxy, ordinary], [PeerFor(ordinary)]));

        Assert.Empty(peers);
    }

    private static List<Machine> AddStudyMachines(ProxyTestFixture fixture)
    {
        var machines = new[] { "p0", "o1", "o2" }
            .Select((id, index) => ProxyTestFixture.Machine(id, $"10.0.0.{index + 1}")).ToList();
        fixture.Machines.Active.AddRange(machines);
        return machines;
    }

    private static TorrentPeerRequest Request(Machine requester, IReadOnlyCollection<Machine> machines,
        IReadOnlyCollection<Peer> available) =>
        new(InfoHash, PeerFor(requester), available, 50, requester, machines);

    private static Peer PeerFor(Machine machine) =>
        new(machine.Id, new IPEndPoint(IPAddress.Parse(machine.IpAddress), 5000), 0, 0, 1);

    private static ZoneTorrentAccessPolicy CreatePolicy(ProxyTestFixture fixture)
    {
        var registry = new SeederRegistry(NullLogger<SeederRegistry>.Instance);
        registry.RegisterSeeder(new FixedSeeder());
        var origin = new CentralSeederPeerProvider(registry,
            Options.Create(new TorrentConfig { TrackerUrl = "http://10.9.0.1:8080/" }),
            NullLogger<CentralSeederPeerProvider>.Instance);
        return new ZoneTorrentAccessPolicy(fixture.Zones, fixture.Election, new FixedArtifactRegistry(), origin,
            NullLogger<ZoneTorrentAccessPolicy>.Instance);
    }

    private sealed class FixedSeeder : ITorrentSeeder
    {
        public Task<List<string>> GetSeededTorrents() => Task.FromResult(new List<string> { InfoHash });
        public string getClientId() => "origin";
        public IPEndPoint GetClientEndpoint() => OriginEndpoint;
    }

    private sealed class FixedArtifactRegistry : ITorrentArtifactRegistry
    {
        public event EventHandler<TorrentArtifact> ArtifactRegistered { add { } remove { } }
        public event EventHandler<TorrentArtifact> ArtifactUnRegistered { add { } remove { } }
        public TorrentArtifact? GetArtifactByInfoHash(string hash) => hash == InfoHash
            ? new TorrentArtifact { ID = ArtifactId, Name = ArtifactId } : null;
        public Task<Dictionary<string, TorrentArtifact>> GetRegisteredArtifacts() => throw new NotSupportedException();
        public Task<TorrentArtifact> CreateAndRegisterTorrentAsync(string name, string description, string filePath,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TorrentArtifact> GetArtifactByIdAsync(string artifactId) => throw new NotSupportedException();
        public Task<string> GetTorrentFilePathAsync(string artifactId) => throw new NotSupportedException();
        public Task<string> GetArtifactContentPathAsync(string artifactId) => throw new NotSupportedException();
        public Task<bool> UnregisterArtifactAsync(string artifactId, bool deleteFiles, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
