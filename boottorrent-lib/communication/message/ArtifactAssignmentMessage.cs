using boottorrent_lib.torrent;
using MessagePack;

namespace boottorrent_lib.communication.message;

public class ArtifactAssignmentMessage : IMqttMessage
{
    public static readonly string MessageType = "artifact_assignment";

    [Key(0)]
    public TorrentJob TorrentJob { get; set; }
    
}