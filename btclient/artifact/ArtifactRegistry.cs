using System.Collections.Concurrent;
using boottorrent_lib.artifact;
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
    private readonly HashSet<string> _loadedFromDiskIds = new(StringComparer.Ordinal);
    public List<ITorrentStatus> ActiveJobs { get; } = new();

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
            if (ActiveJobs.Any(job => job.TorrentJob.ArtifactId == torrentJob.ArtifactId) ||
                (Artifacts.Any(artifact => artifact.ID == torrentJob.ArtifactId) &&
                 !_loadedFromDiskIds.Contains(torrentJob.ArtifactId)))
            {
                logger.LogDebug("Ignoring duplicate assignment for artifact {ArtifactId}.", torrentJob.ArtifactId);
                return;
            }
        
            var fileName = NameUtil.ToFilePathName(torrentJob.Name);
            var artifactDirectoryPath = EnsureArtifactDirectoryExists(torrentJob);
            var artifactMetaDataPath = Path.Combine(artifactDirectoryPath, fileName + ".meta.json");

            var status = await SaveTorrentAndDownload(torrentJob);
            _loadedFromDiskIds.Remove(torrentJob.ArtifactId);
            status.StateChanged += (s, e) =>
            {
                var artifact = Artifacts.FirstOrDefault(a => a.ID == torrentJob.ArtifactId);
                artifact?.State = ConvertTorrentStateToArtifactState(status.State);

                if (artifact?.State == ClientHostedArtifact.ArtifactState.Ready)
                {
                    ActiveJobs.Remove(status);
                }
            };

            var artifact = Artifacts.FirstOrDefault(existing => existing.ID == torrentJob.ArtifactId) ?? new ClientHostedArtifact()
            {
                ID = torrentJob.ArtifactId,
                Name = torrentJob.Name,
                State = ClientHostedArtifact.ArtifactState.Initializing
            };
            artifact.State = ConvertTorrentStateToArtifactState(status.State);
            if (!Artifacts.Contains(artifact)) Artifacts.Add(artifact);
            if (artifact.State != ClientHostedArtifact.ArtifactState.Ready) ActiveJobs.Add(status);
            //Save metadata
            await File.WriteAllTextAsync(artifactMetaDataPath, JsonConvert.SerializeObject(artifact));
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

            ActiveJobs.RemoveAll(job => job.TorrentJob.ArtifactId == artifactId);
            Artifacts.RemoveAll(artifact => artifact.ID == artifactId);
            _loadedFromDiskIds.Remove(artifactId);
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

        Artifacts.Clear();
        _loadedFromDiskIds.Clear();

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
                if (artifact != null && Artifacts.All(a => a.ID != artifact.ID))
                {
                    // Persisted metadata is not proof that a torrent is currently verified and seeding.
                    artifact.State = ClientHostedArtifact.ArtifactState.Initializing;
                    _loadedFromDiskIds.Add(artifact.ID);
                    logger.LogInformation("Found artifact '{ArtifactName}' with ID '{ArtifactId}'.", artifact.Name, artifact.ID);
                    Artifacts.Add(artifact);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to load artifact from '{MetaFile}'.", metaFile);
            }
        }
    }

    private static ClientHostedArtifact.ArtifactState ConvertTorrentStateToArtifactState(ITorrentStatus.TorrentDownloadState statusState)
    {        
        return statusState switch
        {
            ITorrentStatus.TorrentDownloadState.DOWNLOADING => ClientHostedArtifact.ArtifactState.Downloading,
            ITorrentStatus.TorrentDownloadState.DOWNLOADED => ClientHostedArtifact.ArtifactState.Ready,
            _ => ClientHostedArtifact.ArtifactState.Initializing
        };
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
