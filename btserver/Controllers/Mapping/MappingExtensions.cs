using boottorrent_lib.client;
using btserver.Controllers.Dto;

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
    }
}

