namespace btserver.settings;

public class ArtifactSettings
{
    public string TrackerUrl { get; set; }
    
    public string ArtifactStoragePath { get; set; }
    
    public int PieceLength { get; set; } = 256 * 1024; // 256 KB pieces
}