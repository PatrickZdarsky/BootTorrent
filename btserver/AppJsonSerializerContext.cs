using System.Text.Json.Serialization;
using btserver.Controllers.Dto;

namespace btserver;

[JsonSerializable(typeof(List<MachineDto>))]
[JsonSerializable(typeof(MachineDto))]
[JsonSerializable(typeof(PendingArtifactDto))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}

