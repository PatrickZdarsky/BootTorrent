using System.Text.Json.Serialization;
using boottorrent_lib.communication.message;

namespace boottorrent_lib.communication.codec;

[JsonSerializable(typeof(MachineStartedMessage))]
[JsonSerializable(typeof(MachineStoppedMessage))]
[JsonSerializable(typeof(MachineHeartbeatMessage))]
[JsonSerializable(typeof(TorrentAssignmentMessage))]
[JsonSourceGenerationOptions(WriteIndented = false)]
public partial class MqttJsonContext : JsonSerializerContext
{
}