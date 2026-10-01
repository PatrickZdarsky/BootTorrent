using boottorrent_lib.client;
using btclient.torrent.monotorrent;

namespace btclient.Tests;

public class OriginPeerConnectionPolicyTests
{
    [Fact]
    public void OrdinaryClientRejectsOnlyTheCentralSeederEndpoint()
    {
        var policy = new OriginPeerConnectionPolicy();
        policy.Apply(MachineConfiguration.Create([], proxyArtifactIds: [],
            originPeerAddress: "10.0.0.1", originPeerPort: 55123));

        Assert.True(policy.ShouldReject(new Uri("ipv4://10.0.0.1:55123")));
        Assert.False(policy.ShouldReject(new Uri("ipv4://10.0.0.2:55123")));
        Assert.False(policy.ShouldReject(new Uri("ipv4://10.0.0.1:55124")));
    }

    [Fact]
    public void DesignationAndRevocationChangeAccessAndConfigurationHash()
    {
        var policy = new OriginPeerConnectionPolicy();
        var ordinary = MachineConfiguration.Create(["zone"], proxyArtifactIds: [],
            originPeerAddress: "10.0.0.1", originPeerPort: 55123);
        var proxy = MachineConfiguration.Create(["zone"], proxyArtifactIds: ["artifact"],
            originPeerAddress: "10.0.0.1", originPeerPort: 55123);
        var origin = new Uri("ipv4://10.0.0.1:55123");

        Assert.NotEqual(ordinary.ConfigHash, proxy.ConfigHash);
        Assert.False(policy.Apply(ordinary));
        Assert.True(policy.ShouldReject(origin));
        Assert.False(policy.Apply(proxy));
        Assert.False(policy.ShouldReject(origin));
        Assert.True(policy.Apply(ordinary));
        Assert.True(policy.ShouldReject(origin));
    }
}
