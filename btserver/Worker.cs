using boottorrent_lib.torrent;
using btserver.torrent;
using btserver.torrent.impl;
using btserver.torrent.monotorrent;

namespace btserver;

public class Worker(ILogger<Worker> logger, MonoTorrentSeederService seeder, TrackerServer trackerServer, MonoTorrentTracker tracker, TorrentArtifactRegistry registry) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await seeder.StartAsync(stoppingToken);
        // await tracker.StartAsync(stoppingToken);
        await registry.StartAsync(stoppingToken);
        await trackerServer.Start(stoppingToken);
        
        var artifact = (await registry.GetRegisteredArtifacts()).Values.FirstOrDefault();
        if (artifact is not null)
        {
            logger.LogInformation("Seeding existing artifact with ID '{ArtifactId}' and name '{Name}'", artifact.ID, artifact.Name);
            tracker.RegisterSeeder(seeder);
        }
        
        
        while (!stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                //logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }

            await Task.Delay(1000, stoppingToken);
        }
    }
}