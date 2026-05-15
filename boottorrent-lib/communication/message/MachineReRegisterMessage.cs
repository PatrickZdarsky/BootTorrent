using MessagePack;

namespace boottorrent_lib.communication.message;

[MessagePackObject]
public class MachineReRegisterMessage : IMqttMessage
{
    public static readonly string MessageType = "machine_reregister";
}