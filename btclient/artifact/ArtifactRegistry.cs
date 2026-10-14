using System.Security.Cryptography;
using System.Threading.Channels;
using boottorrent_lib.artifact;
using boottorrent_lib.communication.message;
using boottorrent_lib.torrent;
using boottorrent_lib.util;
using btclient.torrent;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using MonoTorrent;

namespace btclient.artifact;

public class ArtifactRegistry(IOptionsMonitor<BTClientSettings> settings, ITorrentClient torrentClient, IHttpClientFactory httpClientFactory, ILogger<ArtifactRegistry> logger)
{
    private readonly SemaphoreSlim _assignmentGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly Dictionary<string, ITorrentStatus> _currentStatuses = new(StringComparer.Ordinal);
    private readonly HashSet<ITorrentStatus> _completionEmitted = new();
    private readonly Channel<ArtifactDownloadCompletedMessage> _completed = Channel.CreateUnbounded<ArtifactDownloadCompletedMessage>();
    public List<ITorrentStatus> ActiveJobs { get; } = new();
    public ChannelReader<ArtifactDownloadCompletedMessage> CompletedDownloads => _completed.Reader;

    public List<ClientHostedArtifact> Artifacts { get; } = new();

    /// <summary>Starts or resumes an assigned torrent and tracks the state reported by the torrent client.</summary>
    /// <remarks>Repeated assignments for active jobs are ignored. Metadata loaded from disk may be resumed because it does not establish that the torrent is ready.</remarks>
    /// <param name="torrentJob">The artifact assignment to add.</param>
    /// <returns>A task for torrent setup and metadata persistence.</returns>
    public async Task AddJob(TorrentJob torrentJob)
    {
        await _assignmentGate.WaitAsync();
        try
        {
            bool duplicate;
            lock (_stateLock)
                duplicate = ActiveJobs.Any(job => job.TorrentJob.ArtifactId == torrentJob.ArtifactId) ||
                    Artifacts.Any(artifact => artifact.ID == torrentJob.ArtifactId &&
                        artifact.State == ClientHostedArtifact.ArtifactState.Ready);
            if (duplicate)
            {
                logger.LogDebug("Ignoring duplicate assignment for artifact {ArtifactId}.", torrentJob.ArtifactId);
                return;
            }

            if (torrentJob.FileSha256 is not { Length: 64 } || !torrentJob.FileSha256.All(Uri.IsHexDigit))
            {
                logger.LogError("Assignment for artifact {ArtifactId} has no valid expected SHA-256.", torrentJob.ArtifactId);
                return;
            }
        
            var fileName = NameUtil.ToFilePathName(torrentJob.Name);
            var artifactDirectoryPath = EnsureArtifactDirectoryExists(torrentJob);
            var artifactMetaDataPath = Path.Combine(artifactDirectoryPath, fileName + ".meta.json");

            var status = await SaveTorrentAndDownload(torrentJob);
            if (status.State == ITorrentStatus.TorrentDownloadState.FAILED)
            {
                logger.LogWarning("Assignment for artifact {ArtifactId} failed and can be retried.", torrentJob.ArtifactId);
                return;
            }
            var verificationGate = new SemaphoreSlim(1, 1);
            status.StateChanged += (_, _) => _ = UpdateStatusAsync(status, verificationGate);

            ClientHostedArtifact artifact;
            lock (_stateLock)
            {
                artifact = Artifacts.FirstOrDefault(existing => existing.ID == torrentJob.ArtifactId) ?? new ClientHostedArtifact()
                {
                    ID = torrentJob.ArtifactId,
                    Name = torrentJob.Name,
                    State = ClientHostedArtifact.ArtifactState.Initializing
                };
                artifact.State = ClientHostedArtifact.ArtifactState.Initializing;
                if (!Artifacts.Contains(artifact)) Artifacts.Add(artifact);
                _currentStatuses[torrentJob.ArtifactId] = status;
                ActiveJobs.Add(status);
            }
            //Save metadata
            await File.WriteAllTextAsync(artifactMetaDataPath, JsonConvert.SerializeObject(artifact));
            _ = UpdateStatusAsync(status, verificationGate);
        }
        finally
        {
            _assignmentGate.Release();
        }
    }

    /// <summary>Stops a client's torrent and removes its local files and registry state.</summary>
    /// <param name="artifactId">The artifact ID to remove.</param>
    /// <returns>A task for torrent removal and file deletion.</returns>
    public async Task RemoveArtifactAsync(string artifactId)
    {
        await _assignmentGate.WaitAsync();
        try
        {
            var artifactDirectory = GetArtifactDirectoryPath(artifactId);
            if (Directory.Exists(artifactDirectory))
            {
                var torrentFile = Directory.GetFiles(artifactDirectory, "*.torrent").FirstOrDefault();
                if (torrentFile is not null)
                {
                    var torrent = await Torrent.LoadAsync(torrentFile);
                    await torrentClient.RemoveTorrentAsync(torrent.InfoHashes.V1OrV2.ToHex());
                }
                Directory.Delete(artifactDirectory, recursive: true);
            }

            lock (_stateLock)
            {
                if (_currentStatuses.Remove(artifactId, out var currentStatus))
                    _completionEmitted.Remove(currentStatus);
                ActiveJobs.RemoveAll(job => job.TorrentJob.ArtifactId == artifactId);
                Artifacts.RemoveAll(artifact => artifact.ID == artifactId);
            }
        }
        finally { _assignmentGate.Release(); }
    }

    /// <summary>Loads saved artifact metadata as initializing until torrent startup verifies its state.</summary>
    public void LoadExistingArtifacts()
    {
        var artifactPath = settings.CurrentValue.ArtifactPath;
        if (!Directory.Exists(artifactPath))
        {
            logger.LogInformation("Artifact directory '{ArtifactPath}' does not exist. Creating it.", artifactPath);
            Directory.CreateDirectory(artifactPath);
            return;
        }
        
        logger.LogInformation("Scanning for existing artifacts in '{ArtifactPath}'.", artifactPath);

        lock (_stateLock)
        {
            Artifacts.Clear();
            _currentStatuses.Clear();
            _completionEmitted.Clear();
            ActiveJobs.Clear();
        }

        var artifactDirectories = Directory.GetDirectories(artifactPath);
        foreach (var artifactDirectory in artifactDirectories)
        {
            var metaFile = Directory.GetFiles(artifactDirectory, "*.meta.json").FirstOrDefault();
            if (metaFile == null)
            {
                logger.LogWarning("No metadata file found in artifact directory '{ArtifactDirectory}'. Skipping.", artifactDirectory);
                continue;
            }

            try
            {
                var json = File.ReadAllText(metaFile);
                var artifact = JsonConvert.DeserializeObject<ClientHostedArtifact>(json);
                if (artifact != null)
                {
                    lock (_stateLock)
                    {
                        if (Artifacts.Any(a => a.ID == artifact.ID)) continue;
                        // Persisted metadata is not proof that a torrent is currently verified and seeding.
                        artifact.State = ClientHostedArtifact.ArtifactState.Initializing;
                        Artifacts.Add(artifact);
                    }
                    logger.LogInformation("Found artifact '{ArtifactName}' with ID '{ArtifactId}'.", artifact.Name, artifact.ID);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to load artifact from '{MetaFile}'.", metaFile);
            }
        }
    }

    /// <summary>Captures completed, active, and locally known artifacts for a heartbeat.</summary>
    public (List<string> Loaded, Dictionary<string, double> Pending, List<string> Known) GetStatusSnapshot()
    {
        lock (_stateLock)
        {
            return (
                Artifacts.Where(artifact => artifact.State == ClientHostedArtifact.ArtifactState.Ready)
                    .Select(artifact => artifact.ID).ToList(),
                ActiveJobs.ToDictionary(job => job.TorrentJob.ArtifactId, job => job.PercentageComplete),
                Artifacts.Select(artifact => artifact.ID).ToList());
        }
    }

    private async Task UpdateStatusAsync(ITorrentStatus status, SemaphoreSlim verificationGate)
    {
        await verificationGate.WaitAsync();
        try
        {
            var artifactId = status.TorrentJob.ArtifactId;
            lock (_stateLock)
            {
                if (!_currentStatuses.TryGetValue(artifactId, out var currentStatus) || currentStatus != status ||
                    Artifacts.All(a => a.ID != artifactId)) return;
                if (status.State != ITorrentStatus.TorrentDownloadState.DOWNLOADED)
                {
                    Artifacts.First(a => a.ID == artifactId).State = status.State == ITorrentStatus.TorrentDownloadState.DOWNLOADING
                        ? ClientHostedArtifact.ArtifactState.Downloading
                        : ClientHostedArtifact.ArtifactState.Initializing;
                    if (!ActiveJobs.Contains(status)) ActiveJobs.Add(status);
                    return;
                }
                if (Artifacts.Any(a => a.ID == artifactId && a.State == ClientHostedArtifact.ArtifactState.Ready)) return;
            }

            var filePath = status.DownloadedFilePath;
            if (filePath is null) throw new InvalidOperationException("Expected a single-file torrent with a downloaded file path.");
            await using var file = File.OpenRead(filePath);
            var actualHash = await SHA256.HashDataAsync(file);
            var expectedHash = Convert.FromHexString(status.TorrentJob.FileSha256);
            if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                throw new InvalidDataException($"SHA-256 mismatch for artifact {artifactId}.");
            if (status.State != ITorrentStatus.TorrentDownloadState.DOWNLOADED) return;

            var completedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (_stateLock)
            {
                var artifact = Artifacts.FirstOrDefault(a => a.ID == artifactId);
                if (!_currentStatuses.TryGetValue(artifactId, out var currentStatus) || currentStatus != status ||
                    artifact is null || artifact.State == ClientHostedArtifact.ArtifactState.Ready) return;
                artifact.State = ClientHostedArtifact.ArtifactState.Ready;
                ActiveJobs.Remove(status);
                if (_completionEmitted.Add(status)) _completed.Writer.TryWrite(new ArtifactDownloadCompletedMessage
                {
                    ArtifactId = artifactId,
                    CompletedAtUnixMilliseconds = completedAt,
                    FileSha256 = Convert.ToHexStringLower(actualHash)
                });
            }
            logger.LogInformation("Artifact {ArtifactId} passed full-file SHA-256 verification.", artifactId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Full-file verification failed for artifact {ArtifactId}.", status.TorrentJob.ArtifactId);
            var shouldStop = false;
            lock (_stateLock)
            {
                if (_currentStatuses.TryGetValue(status.TorrentJob.ArtifactId, out var currentStatus) && currentStatus == status)
                {
                    shouldStop = true;
                    _currentStatuses.Remove(status.TorrentJob.ArtifactId);
                    _completionEmitted.Remove(status);
                    ActiveJobs.Remove(status);
                    var artifact = Artifacts.FirstOrDefault(a => a.ID == status.TorrentJob.ArtifactId);
                    if (artifact is not null) artifact.State = ClientHostedArtifact.ArtifactState.Initializing;
                }
            }
            try
            {
                if (shouldStop && status.InfoHash is not null)
                    await torrentClient.RemoveTorrentAsync(status.InfoHash);
            }
            catch (Exception stopEx)
            {
                logger.LogError(stopEx, "Could not stop unverified torrent for artifact {ArtifactId}.", status.TorrentJob.ArtifactId);
            }
        }
        finally
        {
            verificationGate.Release();
        }
    }

    private async Task<ITorrentStatus> SaveTorrentAndDownload(TorrentJob torrentJob)
    {
        // Create directory with ArtifactID
        var artifactDirectoryPath = EnsureArtifactDirectoryExists(torrentJob);

        // Download torrent file from torrentJob.TorrentFileUrl and save to artifact directory
        byte[] torrentFileBytes;
        try
        {
            using var httpClient = httpClientFactory.CreateClient("torrent-file-downloader");
            torrentFileBytes = await httpClient.GetByteArrayAsync(torrentJob.TorrentFileUrl);
        } catch (Exception e)
        {
            logger.LogError(e, "Failed to download torrent file from URL '{TorrentFileUrl}' for artifact ID '{ArtifactId}'", torrentJob.TorrentFileUrl, torrentJob.ArtifactId);
            return new FailedTorrentStatus {TorrentJob = torrentJob};
        }
        
        
        //Save torrent file to directory
        var torrentFilePath = GetTorrentFilePath(torrentJob.ArtifactId, torrentJob.Name);
        await File.WriteAllBytesAsync(torrentFilePath, torrentFileBytes);
        
        return await torrentClient.AddTorrentAsync(torrentJob, torrentFilePath, artifactDirectoryPath);
    }

    private string EnsureArtifactDirectoryExists(TorrentJob torrentJob)
    {
        var artifactDirectoryPath = GetArtifactDirectoryPath(torrentJob.ArtifactId);
        if (!Directory.Exists(artifactDirectoryPath))
        {
            Directory.CreateDirectory(artifactDirectoryPath);
        }

        return artifactDirectoryPath;
    }

    protected string GetArtifactDirectoryPath(string artifactId)
    {
        return Path.Combine(settings.CurrentValue.ArtifactPath, artifactId);
    }

    private string GetTorrentFilePath(string artifactId, string artifactName)
    {
        return Path.Combine(settings.CurrentValue.ArtifactPath, artifactId, $"{NameUtil.ToFilePathName(artifactName)}.torrent");
    }
}
