using boottorrent_lib.torrent;
using btserver.torrent;
using btserver.torrent.impl;
using btserver.torrent.monotorrent;
using btserver.torrent.tracker;

namespace btserver;

public class Worker(ILogger<Worker> logger, MonoTorrentSeederService seeder, TrackerServer trackerServer, MonoTorrentTracker tracker, TorrentArtifactRegistry registry) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await seeder.StartAsync(stoppingToken);
        // await tracker.StartAsync(stoppingToken);
        await registry.StartAsync(stoppingToken);
        trackerServer.Start(stoppingToken);
        
        var artifact = (await registry.GetRegisteredArtifacts()).Values.FirstOrDefault();

        
        
        if (artifact is not null)
        {
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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        trackerServer.Stop();
        await base.StopAsync(cancellationToken);
    }
}