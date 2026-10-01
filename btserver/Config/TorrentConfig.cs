namespace btserver.Config;

public class TorrentConfig
{
    /// <summary>Seconds a tracker client should wait between announces.</summary>
    public int AnnounceIntervalSeconds { get; set; } = 30;
    /// <summary>Seconds since the last announce before a tracker peer expires.</summary>
    public int PeerExpirationSeconds { get; set; } = 120;
    public string TrackerUrl { get; set; }
    
    public string ArtifactStoragePath { get; set; }

    public string ArtifactUploadRoot { get; set; }
    
    public int PieceLength { get; set; } = 256 * 1024; // 256 KB pieces
    
    //Tracker related stuff
    public string TrackerId { get; set; }
    
    public string TrackerBindAddress { get; set; }
    
    public int TrackerPort { get; set; }

    public string AnnounceSuffix { get; set; } = "announce/";
    
    public string TorrentFileGetSuffix { get; set; } = "torrent/";
}
