using MessagePack;

namespace boottorrent_lib.communication.message;

[MessagePackObject]
public class MachineStoppedMessage : IMqttMessage
{
    public static readonly string MessageType = "shutdown";
    
    [Key(0)]
    public string IPAddress { get; set; }
}