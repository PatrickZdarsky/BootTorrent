using MessagePack;

namespace boottorrent_lib.transport.message;

[MessagePackObject]
public class MachineStartedMessage
{
    public static readonly string MessageSuffix = "startup";
    
    [Key(0)]
    public string IPAddress { get; set; }
}