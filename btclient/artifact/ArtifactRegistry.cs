using System.Collections.Concurrent;
using boottorrent_lib.artifact;
using boottorrent_lib.torrent;
using boottorrent_lib.util;
using btclient.torrent;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace btclient.artifact;

public class ArtifactRegistry(IOptionsMonitor<BTClientSettings> settings, ITorrentClient torrentClient, IHttpClientFactory httpClientFactory, ILogger<ArtifactRegistry> logger)
{
    public List<ITorrentStatus> ActiveJobs { get; } = new();

    public List<ClientHostedArtifact> Artifacts { get; } = new();

    public async Task AddJob(TorrentJob torrentJob)
    {
        if (ActiveJobs.FirstOrDefault(j => j.TorrentJob.ArtifactId == torrentJob.ArtifactId) != null)
        {
            throw new InvalidOperationException($"A job with artifact ID {torrentJob.ArtifactId} is already active.");
        }
        
        var fileName = NameUtil.ToFilePathName(torrentJob.Name);
        var artifactDirectoryPath = EnsureArtifactDirectoryExists(torrentJob);
        var artifactMetaDataPath = Path.Combine(artifactDirectoryPath, fileName + ".meta.json");
        
        
        var status = await SaveTorrentAndDownload(torrentJob);
        status.StateChanged += (s, e) =>
        {
            var artifact = Artifacts.FirstOrDefault(a => a.ID == torrentJob.ArtifactId);
            artifact?.State = ConvertTorrentStateToArtifactState(status.State);

            if (artifact?.State == ClientHostedArtifact.ArtifactState.Ready)
            {
                ActiveJobs.Remove(status);
            }
        };
        
        ActiveJobs.Add(status);
        var artifact = new ClientHostedArtifact()
        {
            ID = torrentJob.ArtifactId,
            Name = torrentJob.Name,
            State = ClientHostedArtifact.ArtifactState.Initializing
        };
        Artifacts.Add(artifact);
        //Save metadata
        await File.WriteAllTextAsync(artifactMetaDataPath, JsonConvert.SerializeObject(artifact));
    }

    public void RemoveArtifact(string artifactId)
    {
        var artifact = Artifacts.FirstOrDefault(a => a.ID == artifactId);
        
        if (artifact is null)
            return;
        
        Directory.Delete(GetArtifactDirectoryPath(artifactId), true);
        Artifacts.Remove(artifact);
    }

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
        using (var httpClient = httpClientFactory.CreateClient("torrent-file-downloader"))
        {
            torrentFileBytes = await httpClient.GetByteArrayAsync(torrentJob.TorrentFileUrl);
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