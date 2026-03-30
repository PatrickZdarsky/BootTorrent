using System.Collections.Concurrent;

namespace btserver.torrent.impl;

public class SeederRegistry : ISeederRegistry
{
    private readonly ConcurrentDictionary<ITorrentSeeder, List<string>> _seederTorrents = new();
    private readonly ILogger<SeederRegistry> _logger;

    public SeederRegistry(ILogger<SeederRegistry> logger)
    {
        _logger = logger;
    }

    public void RegisterSeeder(ITorrentSeeder seeder)
    {
        _seederTorrents.TryAdd(seeder, new List<string>());
        _logger.LogInformation("Registered seeder {SeederEndpoint}", seeder.GetClientEndpoint());
    }

    public void UnregisterSeeder(ITorrentSeeder seeder)
    {
        _seederTorrents.TryRemove(seeder, out _);
        _logger.LogInformation("Unregistered seeder {SeederEndpoint}", seeder.GetClientEndpoint());
    }

    public async Task<List<ITorrentSeeder>> GetSeedersForTorrent(string infoHash)
    {
        var capableSeeders = new List<ITorrentSeeder>();
        var normalizedInfoHash = infoHash.Trim().ToLowerInvariant();

        foreach (var seeder in _seederTorrents.Keys)
        {
            var seededTorrents = await seeder.GetSeededTorrents();
            if (seededTorrents.Any(t => t.Trim().ToLowerInvariant() == normalizedInfoHash))
            {
                capableSeeders.Add(seeder);
            }
        }

        return capableSeeders;
    }
}

