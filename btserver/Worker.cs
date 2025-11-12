using btserver.torrent;

namespace btserver;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    
    private readonly ITorrentCreator _torrentCreator;

    public Worker(ILogger<Worker> logger, ITorrentCreator torrentCreator)
    {
        _logger = logger;
        _torrentCreator = torrentCreator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _torrentCreator.GenerateTorrentArtifactAsync("SC_Contract", "ShiftControl Contract",
            "..\\app\\ShiftControl-Contract.pdf");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }

            await Task.Delay(1000, stoppingToken);
        }
    }
}