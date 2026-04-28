using boottorrent_lib.client;

namespace boottorrent_lib.communication.message;

/// <summary>
/// Server => Client sending the desired configuration of the machine.
/// This is sent when the client first connects to the server, and whenever the server changes the configuration of the machine.
/// </summary>
public class MachineConfigurationMessage : IMqttMessage
{
    public static readonly string MessageType = "machine_configuration";
    
    public MachineConfiguration Configuration { get; set; }
}