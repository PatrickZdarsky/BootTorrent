using boottorrent_lib.client;
using btserver.Controllers.Dto;
using btserver.Data;
using btserver.Zone;
using boottorrent_lib.torrent;

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
                ManagementZoneIds = machine.AssignedZones,
                DistributionZoneId = machine.DistributionZoneId,
                ConflictingDistributionZoneIds = machine.ConflictingDistributionZoneIds,
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
                    IsDistributionZone = subnetZone.IsDistributionZone,
                    AssignedArtifactIds = subnetZone.AssignedArtifactIds,
                    Subnet = subnetZone.Subnet
                },
                StaticZone staticZone => new ZoneDto
                {
                    Id = staticZone.Id,
                    Type = "static",
                    Name = staticZone.Name,
                    IsDistributionZone = staticZone.IsDistributionZone,
                    AssignedArtifactIds = staticZone.AssignedArtifactIds,
                    MachineIds = staticZone.MachineIds
                },
                _ => throw new InvalidOperationException($"Unsupported zone type {zone.GetType().Name}.")
            };
        }

        public static ArtifactDto ToDto(this TorrentArtifact artifact, IEnumerable<Guid> assignedZoneIds)
        {
            return new ArtifactDto
            {
                Id = artifact.ID,
                Name = artifact.Name,
                InfoHashV1 = artifact.InfoHashV1,
                InfoHashV2 = artifact.InfoHashV2,
                TorrentFileUrl = artifact.TorrentFileUrl,
                AssignedZoneIds = assignedZoneIds.ToList()
            };
        }
    }
}
