using boottorrent_lib.client;
using btserver.Controllers.Dto;
using btserver.Data;
using btserver.Zone;

namespace btserver.Controllers.Mapping
{
    public static class MappingExtensions
    {
        public static MachineDto ToDto(this Machine machine)
        {
            return new MachineDto
            {
                Id = machine.Id,
                IpAddress = machine.IpAddress,
                LoadedArtifacts = machine.LoadedArtifacts,
                PendingArtifacts = machine.PendingArtifacts.Select(kv => new PendingArtifactDto
                {
                    Name = kv.Key,
                    Progress = kv.Value
                }).ToList()
            };
        }

        public static ZoneDto ToDto(this Zone.Zone zone)
        {
            return zone switch
            {
                SubnetZone subnetZone => new ZoneDto
                {
                    Id = subnetZone.Id,
                    Type = "subnet",
                    Name = subnetZone.Name,
                    AssignedArtifactIds = subnetZone.AssignedArtifactIds,
                    Subnet = subnetZone.Subnet
                },
                StaticZone staticZone => new ZoneDto
                {
                    Id = staticZone.Id,
                    Type = "static",
                    Name = staticZone.Name,
                    AssignedArtifactIds = staticZone.AssignedArtifactIds,
                    MachineIds = staticZone.MachineIds
                },
                _ => throw new InvalidOperationException($"Unsupported zone type {zone.GetType().Name}.")
            };
        }

        public static SubnetZonePolicyZoneConfigurationDto ToDto(this SubnetZonePolicyConfiguration configuration, string zoneName)
        {
            return new SubnetZonePolicyZoneConfigurationDto
            {
                ZoneId = configuration.ZoneId,
                ZoneName = zoneName,
                ProxyCount = configuration.ProxyCount,
                ProxyMachineIds = configuration.ProxyMachineIds
            };
        }
    }
}
