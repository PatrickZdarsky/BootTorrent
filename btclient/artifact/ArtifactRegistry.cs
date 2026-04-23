using System.Collections.Concurrent;
using boottorrent_lib.artifact;
using boottorrent_lib.torrent;
using boottorrent_lib.util;
using btclient.torrent;
using Microsoft.Extensions.Options;

namespace btclient.artifact;

public class ArtifactRegistry
{
    private readonly IOptionsMonitor<BTClientSettings> _settings;
    private readonly ITorrentClient _torrentClient;
    private readonly IHttpClientFactory _httpClientFactory;

    public ArtifactRegistry(IOptionsMonitor<BTClientSettings> settings, ITorrentClient torrentClient, IHttpClientFactory httpClientFactory)
    {
        _settings = settings;
        _torrentClient = torrentClient;
        _httpClientFactory = httpClientFactory;
    }
    
    public List<ITorrentStatus> ActiveJobs { get; } = new();

    public List<ClientHostedArtifact> Artifacts { get; } = new();

    public async Task AddJob(TorrentJob torrentJob)
    {
        if (ActiveJobs.FirstOrDefault(j => j.TorrentJob.ArtifactId == torrentJob.ArtifactId) != null)
        {
            throw new InvalidOperationException($"A job with artifact ID {torrentJob.ArtifactId} is already active.");
        }
        
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
        Artifacts.Add(new ClientHostedArtifact()
        {
            ID =  torrentJob.ArtifactId,
            Name = torrentJob.Name,
            State = ClientHostedArtifact.ArtifactState.Initializing
        });
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
        var artifactDirectoryPath = Path.Combine(_settings.CurrentValue.ArtifactPath, torrentJob.ArtifactId);
        if (!Directory.Exists(artifactDirectoryPath))
        {
            Directory.CreateDirectory(artifactDirectoryPath);
        }
        
        // Download torrent file from torrentJob.TorrentFileUrl and save to artifact directory
        byte[] torrentFileBytes;
        using (var httpClient = _httpClientFactory.CreateClient("torrent-file-downloader"))
        {
            torrentFileBytes = await httpClient.GetByteArrayAsync(torrentJob.TorrentFileUrl);
        }
        
        //Save torrent file to directory
        var torrentFilePath = GetTorrentFilePath(torrentJob.ArtifactId, torrentJob.Name);
        await File.WriteAllBytesAsync(torrentFilePath, torrentFileBytes);
        
        return await _torrentClient.AddTorrentAsync(torrentJob, torrentFilePath, artifactDirectoryPath);
    }

    private string GetTorrentFilePath(string artifactId, string artifactName)
    {
        return Path.Combine(_settings.CurrentValue.ArtifactPath, artifactId, $"{NameUtil.ToFilePathName(artifactName)}.torrent");
    }
}