using MessagePack;

namespace boottorrent_lib.communication.message;

[MessagePackObject]
public class MachineStartedMessage : IMqttMessage
{
    public static readonly string MessageType = "startup";
    
    [Key(0)]
    public string IPAddress { get; set; }
}