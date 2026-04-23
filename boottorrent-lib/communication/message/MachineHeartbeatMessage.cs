using MessagePack;

namespace boottorrent_lib.communication.message;

[MessagePackObject]
public class MachineHeartbeatMessage : IMqttMessage
{
    public static readonly string MessageType = "heartbeat";
    
    [Key(0)]
    public long Timestamp { get; set; }
    [Key(1)]
    public List<string> LoadedArtifacts { get; set; }
    [Key(2)]
    public Dictionary<string, double> PendingArtifacts { get; set; }
}