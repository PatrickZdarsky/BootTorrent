using boottorrent_lib.torrent;
using btserver.torrent;

namespace btserver;

public class Worker(ILogger<Worker> logger, ITorrentCreator torrentCreator, ITorrentSeederService torrentSeederService, ITorrentArtifactRegistry registry) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(1000, stoppingToken);
        var artifact = (await registry.GetRegisteredArtifacts()).Values.FirstOrDefault();
        if (artifact is not null)
        {
            logger.LogInformation("Seeding existing artifact with ID '{ArtifactId}' and name '{Name}'", artifact.ID, artifact.Name);
            await torrentSeederService.EnsureSeedingAsync(artifact.ID, stoppingToken);
        }
        
        
        while (!stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }

            await Task.Delay(1000, stoppingToken);
        }
    }
}