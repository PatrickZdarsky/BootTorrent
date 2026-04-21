namespace boottorrent_lib.artifact;

public class ClientHostedArtifact : Artifact
{
    public ArtifactState State { get; set; }
    
    public enum ArtifactState { Initializing, Downloading, Ready }
}