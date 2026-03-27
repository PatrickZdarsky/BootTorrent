namespace btserver.settings;

public class TorrentSettings
{
    public string TrackerUrl { get; set; }
    
    public string ArtifactStoragePath { get; set; }
    
    public int PieceLength { get; set; } = 256 * 1024; // 256 KB pieces
    
    //Tracker related stuff
    public string TrackerId { get; set; }
    
    public string TrackerBindAddress { get; set; }
    
    public int TrackerPort { get; set; }
}