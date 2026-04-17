using boottorrent_lib.torrent;
using btserver.torrent;
using btserver.torrent.impl;
using btserver.torrent.monotorrent;
using btserver.torrent.tracker;

namespace btserver;

public class Worker(ILogger<Worker> logger, MonoTorrentSeederService seeder, TrackerServer trackerServer, TorrentArtifactRegistry registry) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker starting at: {time}", DateTimeOffset.Now);
        await seeder.StartAsync(stoppingToken);
        await registry.StartAsync(stoppingToken);
        trackerServer.Start(stoppingToken);

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