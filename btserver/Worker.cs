using boottorrent_lib.torrent;
using btserver.Swarm;
using btserver.torrent;
using btserver.torrent.impl;
using btserver.torrent.monotorrent;
using btserver.torrent.tracker;

namespace btserver;

public class Worker(ILogger<Worker> logger, MonoTorrentSeederService seeder, TrackerServer trackerServer, TorrentArtifactRegistry registry, ISeederRegistry seederRegistry) : BackgroundService
{
    /// <summary>Starts the seeder, restores registered artifacts, and starts the tracker.</summary>
    /// <param name="stoppingToken">Signals host shutdown.</param>
    /// <returns>A task for the worker lifetime.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker starting at: {time}", DateTimeOffset.Now);
        await seeder.StartAsync(stoppingToken);
        await registry.StartAsync(stoppingToken);
        foreach (var artifact in (await registry.GetRegisteredArtifacts()).Values)
        {
            await seeder.EnsureSeedingAsync(artifact.ID, stoppingToken);
        }
        seederRegistry.RegisterSeeder(seeder);
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
        seederRegistry.UnregisterSeeder(seeder);
        await base.StopAsync(cancellationToken);
    }
}
