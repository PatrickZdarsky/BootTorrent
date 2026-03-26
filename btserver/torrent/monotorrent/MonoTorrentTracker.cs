using btserver.settings;
using Microsoft.Extensions.Options;
using MonoTorrent.TrackerServer;

namespace btserver.torrent.monotorrent;

public class MonoTorrentTracker
{
    private TrackerServer? _trackerServer;
    
    private readonly IOptions<TrackerSettings> settings;
    private readonly ITorrentTrackerHandler _trackerHandler;
    
    public MonoTorrentTracker(IOptions<TrackerSettings> settings, ITorrentTrackerHandler trackerHandler)
    {
        this.settings = settings;
        this._trackerHandler = trackerHandler;
    }

    public Task Setup()
    {
        _trackerServer = new TrackerServer(settings.Value.TrackerId);
        
        //Use Peer ID for trackerHandler
        _trackerServer.RegisterListener(new );
    }
}