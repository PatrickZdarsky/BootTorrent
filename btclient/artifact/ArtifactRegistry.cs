using System.Collections.Concurrent;
using boottorrent_lib.torrent;
using boottorrent_lib.util;
using btclient.torrent;
using Microsoft.Extensions.Options;

namespace btclient.artifact;

public class ArtifactRegistry
{
    private readonly BlockingCollection<TorrentJob> _activeJobs = new();
    
    
    private readonly IOptionsMonitor<BTClientSettings> _settings;
    private readonly ITorrentClient _torrentClient;

    public ArtifactRegistry(IOptionsMonitor<BTClientSettings> settings, ITorrentClient torrentClient)
    {
        _settings = settings;
        _torrentClient = torrentClient;
    }

    public async Task AddJob(TorrentJob torrentJob)
    {
        if (_activeJobs.FirstOrDefault(j => j.ArtifactId == torrentJob.ArtifactId) != null)
        {
            throw new InvalidOperationException($"A job with artifact ID {torrentJob.ArtifactId} is already active.");
        }
        
        _activeJobs.Add(torrentJob);
        
        await SaveTorrentAndDownload(torrentJob);
    }

    private async Task SaveTorrentAndDownload(TorrentJob torrentJob)
    {
        // Create directory with ArtifactID
        var artifactDirectoryPath = Path.Combine(_settings.CurrentValue.ArtifactPath, torrentJob.ArtifactId);
        if (!Directory.Exists(artifactDirectoryPath))
        {
            Directory.CreateDirectory(artifactDirectoryPath);
        }
        
        //Save torrent file to directory
        var torrentFilePath = GetTorrentFilePath(torrentJob.Artifact.Name);
        File.WriteAllBytes(torrentFilePath, torrentJob.Artifact.Torrent.TorrentFileBytes);
        
        await _torrentClient.AddTorrentAsync(torrentFilePath, artifactDirectoryPath);
    }

    private string GetTorrentFilePath(string artifactName)
    {
        return Path.Combine(_settings.CurrentValue.ArtifactPath, $"{NameUtil.ToFilePathName(artifactName)}.torrent");
    }
}