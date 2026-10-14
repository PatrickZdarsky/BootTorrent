using System.Net;
using System.Security.Cryptography;
using boottorrent_lib.artifact;
using boottorrent_lib.communication.codec;
using boottorrent_lib.communication.message;
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
    public void CompletionMessageRoundTripsThroughMqttCodec()
    {
        var codec = new JsonMessageCodec();
        var message = new ArtifactDownloadCompletedMessage
        {
            ArtifactId = "artifact", CompletedAtUnixMilliseconds = 123456,
            FileSha256 = new string('a', 64)
        };
        var decoded = codec.Decode<ArtifactDownloadCompletedMessage>(codec.Encode(message));
        Assert.Equal(message.ArtifactId, decoded.ArtifactId);
        Assert.Equal(message.CompletedAtUnixMilliseconds, decoded.CompletedAtUnixMilliseconds);
        Assert.Equal(message.FileSha256, decoded.FileSha256);
    }

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
                ArtifactId = "artifact", Name = "study", TorrentFileUrl = "https://example.invalid/torrent",
                FileSha256 = Convert.ToHexStringLower(SHA256.HashData([1, 2, 3]))
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

    [Fact]
    public async Task CompletionRequiresFullFileSha256AndIsEmittedOnce()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"boottorrent-client-test-{Guid.NewGuid()}");
        try
        {
            var torrentClient = new FakeTorrentClient();
            var registry = new ArtifactRegistry(new FixedOptionsMonitor(new BTClientSettings { ArtifactPath = directory }),
                torrentClient, new AlwaysSuccessfulHttpClientFactory(), NullLogger<ArtifactRegistry>.Instance);
            var job = new TorrentJob
            {
                ArtifactId = "artifact", Name = "study", TorrentFileUrl = "https://example.invalid/torrent",
                FileSha256 = Convert.ToHexStringLower(SHA256.HashData([1, 2, 3]))
            };

            await registry.AddJob(job);
            Assert.Empty(registry.GetStatusSnapshot().Loaded);
            torrentClient.Status!.SetState(ITorrentStatus.TorrentDownloadState.DOWNLOADED);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var completed = await registry.CompletedDownloads.ReadAsync(timeout.Token);

            Assert.Equal("artifact", completed.ArtifactId);
            Assert.Equal(job.FileSha256, completed.FileSha256);
            Assert.True(completed.CompletedAtUnixMilliseconds > 0);
            Assert.Equal("artifact", Assert.Single(registry.GetStatusSnapshot().Loaded));
            torrentClient.Status.SetState(ITorrentStatus.TorrentDownloadState.WAITING);
            Assert.True(SpinWait.SpinUntil(() => registry.GetStatusSnapshot().Loaded.Count == 0,
                TimeSpan.FromSeconds(5)));
            Assert.True(registry.GetStatusSnapshot().Pending.ContainsKey("artifact"));
            torrentClient.Status.SetState(ITorrentStatus.TorrentDownloadState.DOWNLOADED);
            Assert.True(SpinWait.SpinUntil(() => registry.GetStatusSnapshot().Loaded.Count == 1,
                TimeSpan.FromSeconds(5)));
            Assert.False(registry.CompletedDownloads.TryRead(out _));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AlreadySeedingTorrentIsVerifiedBeforeReportingCompletion()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"boottorrent-client-test-{Guid.NewGuid()}");
        try
        {
            var torrentClient = new FakeTorrentClient { InitialState = ITorrentStatus.TorrentDownloadState.DOWNLOADED };
            var registry = new ArtifactRegistry(new FixedOptionsMonitor(new BTClientSettings { ArtifactPath = directory }),
                torrentClient, new AlwaysSuccessfulHttpClientFactory(), NullLogger<ArtifactRegistry>.Instance);
            await registry.AddJob(new TorrentJob
            {
                ArtifactId = "artifact", Name = "study", TorrentFileUrl = "https://example.invalid/torrent",
                FileSha256 = Convert.ToHexStringLower(SHA256.HashData([1, 2, 3]))
            });

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.Equal("artifact", (await registry.CompletedDownloads.ReadAsync(timeout.Token)).ArtifactId);
            Assert.Equal("artifact", Assert.Single(registry.GetStatusSnapshot().Loaded));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Sha256MismatchStopsTorrentWithoutReportingCompletion()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"boottorrent-client-test-{Guid.NewGuid()}");
        try
        {
            var torrentClient = new FakeTorrentClient();
            var registry = new ArtifactRegistry(new FixedOptionsMonitor(new BTClientSettings { ArtifactPath = directory }),
                torrentClient, new AlwaysSuccessfulHttpClientFactory(), NullLogger<ArtifactRegistry>.Instance);
            await registry.AddJob(new TorrentJob
            {
                ArtifactId = "artifact", Name = "study", TorrentFileUrl = "https://example.invalid/torrent",
                FileSha256 = new string('0', 64)
            });

            torrentClient.Status!.SetState(ITorrentStatus.TorrentDownloadState.DOWNLOADED);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await torrentClient.Removed.Task.WaitAsync(timeout.Token);

            Assert.Empty(registry.GetStatusSnapshot().Loaded);
            Assert.Empty(registry.GetStatusSnapshot().Pending);
            Assert.False(registry.CompletedDownloads.TryRead(out _));
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

    private sealed class AlwaysSuccessfulHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new SuccessHandler());
        private sealed class SuccessHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        }
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
        public ITorrentStatus.TorrentDownloadState InitialState { get; init; } = ITorrentStatus.TorrentDownloadState.DOWNLOADING;
        public FakeTorrentStatus? Status { get; private set; }
        public TaskCompletionSource Removed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ITorrentStatus> AddTorrentAsync(TorrentJob job, string torrentFilePath, string downloadPath)
        {
            AddCalls++;
            var filePath = Path.Combine(downloadPath, "study.bin");
            File.WriteAllBytes(filePath, [1, 2, 3]);
            Status = new FakeTorrentStatus(job, filePath);
            Status.SetState(InitialState);
            return Task.FromResult<ITorrentStatus>(Status);
        }
        public Task RemoveTorrentAsync(string infoHash)
        {
            Removed.TrySetResult();
            return Task.CompletedTask;
        }
        public Task StartAsync() => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public List<ITorrentStatus> GetActiveTorrents() => [];
    }

    private sealed class FakeTorrentStatus(TorrentJob job, string filePath) : ITorrentStatus
    {
        public event EventHandler? StateChanged;
        public TorrentJob TorrentJob => job;
        public string? DownloadedFilePath => filePath;
        public string? InfoHash => "fake-info-hash";
        public double PercentageComplete => 0;
        public ITorrentStatus.TorrentDownloadState State { get; private set; } = ITorrentStatus.TorrentDownloadState.DOWNLOADING;
        public void SetState(ITorrentStatus.TorrentDownloadState state)
        {
            State = state;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
