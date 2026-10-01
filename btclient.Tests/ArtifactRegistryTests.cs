using System.Net;
using boottorrent_lib.artifact;
using boottorrent_lib.torrent;
using btclient;
using btclient.artifact;
using btclient.torrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace btclient.Tests;

public class ArtifactRegistryTests
{
    [Fact]
    public async Task FailedTorrentMetadataDownloadCanBeRetried()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"boottorrent-client-test-{Guid.NewGuid()}");
        try
        {
            var torrentClient = new FakeTorrentClient();
            var registry = new ArtifactRegistry(new FixedOptionsMonitor(new BTClientSettings { ArtifactPath = directory }),
                torrentClient, new FakeHttpClientFactory(), NullLogger<ArtifactRegistry>.Instance);
            var job = new TorrentJob
            {
                ArtifactId = "artifact", Name = "study", TorrentFileUrl = "https://example.invalid/torrent"
            };

            await registry.AddJob(job);
            Assert.Empty(registry.GetStatusSnapshot().Known);
            Assert.Empty(registry.GetStatusSnapshot().Pending);

            await registry.AddJob(job);
            var status = registry.GetStatusSnapshot();
            Assert.Equal("artifact", Assert.Single(status.Known));
            Assert.True(status.Pending.ContainsKey("artifact"));
            Assert.Equal(1, torrentClient.AddCalls);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RestoredMetadataIsReportedAsKnownButNotComplete()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"boottorrent-client-test-{Guid.NewGuid()}");
        try
        {
            var artifactDirectory = Path.Combine(directory, "artifact");
            Directory.CreateDirectory(artifactDirectory);
            File.WriteAllText(Path.Combine(artifactDirectory, "study.meta.json"),
                JsonConvert.SerializeObject(new ClientHostedArtifact
                {
                    ID = "artifact", Name = "study", State = ClientHostedArtifact.ArtifactState.Ready
                }));
            var registry = new ArtifactRegistry(new FixedOptionsMonitor(new BTClientSettings { ArtifactPath = directory }),
                new FakeTorrentClient(), new FakeHttpClientFactory(), NullLogger<ArtifactRegistry>.Instance);

            registry.LoadExistingArtifacts();

            var status = registry.GetStatusSnapshot();
            Assert.Equal("artifact", Assert.Single(status.Known));
            Assert.Empty(status.Loaded);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FixedOptionsMonitor(BTClientSettings value) : IOptionsMonitor<BTClientSettings>
    {
        public BTClientSettings CurrentValue => value;
        public BTClientSettings Get(string? name) => value;
        public IDisposable? OnChange(Action<BTClientSettings, string?> listener) => null;
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly RetryHandler _handler = new();
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class RetryHandler : HttpMessageHandler
    {
        private int _calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
                throw new HttpRequestException("Temporary download failure.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3])
            });
        }
    }

    private sealed class FakeTorrentClient : ITorrentClient
    {
        public int AddCalls { get; private set; }
        public Task<ITorrentStatus> AddTorrentAsync(TorrentJob job, string torrentFilePath, string downloadPath)
        {
            AddCalls++;
            return Task.FromResult<ITorrentStatus>(new FakeTorrentStatus(job));
        }
        public Task RemoveTorrentAsync(string infoHash) => Task.CompletedTask;
        public Task StartAsync() => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public List<ITorrentStatus> GetActiveTorrents() => [];
    }

    private sealed class FakeTorrentStatus(TorrentJob job) : ITorrentStatus
    {
        public event EventHandler? StateChanged { add { } remove { } }
        public TorrentJob TorrentJob => job;
        public double PercentageComplete => 0;
        public ITorrentStatus.TorrentDownloadState State => ITorrentStatus.TorrentDownloadState.DOWNLOADING;
    }
}
