using MessagePack;

namespace boottorrent_lib.communication.message;

public class ArtifactUnassignmentMessage : IMqttMessage
{
    public static readonly string MessageType = "artifact_unassignment";
    
    [Key(0)]
    public string ArtifactId { get; set; }
}