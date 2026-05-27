using System.Text.Json.Serialization;
using btserver.Controllers.Dto;

namespace btserver;

[JsonSerializable(typeof(List<MachineDto>))]
[JsonSerializable(typeof(MachineDto))]
[JsonSerializable(typeof(PendingArtifactDto))]
[JsonSerializable(typeof(List<ZoneDto>))]
[JsonSerializable(typeof(ZoneDto))]
[JsonSerializable(typeof(UpsertZoneRequestDto))]
[JsonSerializable(typeof(List<PolicyDto>))]
[JsonSerializable(typeof(PolicyDto))]
[JsonSerializable(typeof(SubnetZonePolicyDto))]
[JsonSerializable(typeof(SubnetZonePolicyZoneConfigurationDto))]
[JsonSerializable(typeof(UpsertSubnetZonePolicyConfigurationRequestDto))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}
