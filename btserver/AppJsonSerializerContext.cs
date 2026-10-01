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
[JsonSerializable(typeof(ProxyZoneConfigurationDto))]
[JsonSerializable(typeof(UpdateProxyZoneConfigurationRequestDto))]
[JsonSerializable(typeof(SetProxyDesignationsRequestDto))]
[JsonSerializable(typeof(ProxyStatusDto))]
[JsonSerializable(typeof(ProxyDesignationDto))]
[JsonSerializable(typeof(ProxyTransitionDto))]
[JsonSerializable(typeof(List<ProxyTransitionDto>))]
[JsonSerializable(typeof(List<ArtifactDto>))]
[JsonSerializable(typeof(ArtifactDto))]
[JsonSerializable(typeof(CreateArtifactRequestDto))]
[JsonSerializable(typeof(ZoneArtifactAssignmentResultDto))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}
