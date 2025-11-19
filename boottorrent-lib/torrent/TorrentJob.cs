namespace boottorrent_lib.torrent;

public class TorrentJob
{
    public String JobId => $"{ArtifactId}@{SavePath}";
    public String ArtifactId => Artifact.ID;
    public TorrentArtifact Artifact { get; init; } = default!;
    public string SavePath { get; init; } = default!;
    public DestinationSelector DestinationSelector { get; init; } = default!;
}