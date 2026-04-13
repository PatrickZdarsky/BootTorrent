using MessagePack;

namespace boottorrent_lib.communication.message;

[MessagePackObject]
public class MachineHeartbeatMessage
{
    public static readonly string MessageType = "heartbeat";
    
    [Key(0)]
    public string MachineId { get; set; }
    [Key(1)]
    public long Timestamp { get; set; }
    [Key(2)]
    public List<string> LoadedArtifacts { get; set; }
}