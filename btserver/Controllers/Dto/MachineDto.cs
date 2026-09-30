using Swashbuckle.AspNetCore.Annotations;

namespace btserver.Controllers.Dto
{
    [SwaggerSchema(Description = "Represents a single machine in the swarm.")]
    public class MachineDto
    {
        [SwaggerSchema(Description = "The unique identifier of the machine.")]
        public string Id { get; set; }
        [SwaggerSchema(Description = "The IP address of the machine.")]
        public string IpAddress { get; set; }
        [SwaggerSchema(Description = "All matching management zone IDs, including the computed default zone when applicable.")]
        public List<string> ManagementZoneIds { get; set; } = [];
        [SwaggerSchema(Description = "The single resolved distribution zone ID, or null if zone assignments conflict.")]
        public string? DistributionZoneId { get; set; }
        [SwaggerSchema(Description = "Distribution zone IDs that conflict for this machine, if any.")]
        public List<string> ConflictingDistributionZoneIds { get; set; } = [];
        [SwaggerSchema(Description = "A list of artifacts that are fully downloaded and loaded on the machine.")]
        public List<string> LoadedArtifacts { get; set; }
        [SwaggerSchema(Description = "A list of artifacts that are currently being downloaded by the machine.")]
        public List<PendingArtifactDto> PendingArtifacts { get; set; }
    }
}
