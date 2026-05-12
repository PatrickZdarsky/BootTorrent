using boottorrent_lib.client;
using boottorrent_lib.communication;
using boottorrent_lib.communication.message;
using boottorrent_lib.torrent;
using btserver.Data;
using btserver.torrent;

namespace btserver.Swarm;

public class ArtifactAssigner(ILogger<ArtifactAssigner> logger,
    ITorrentArtifactRegistry artifactRegistry, 
    BtDbContext dbContext, Lazy<ServerMqttService> mqttService)
{
    private async Task Process(Machine machine)
    {
        var assignedArtifacts = GetAllAssignedArtifactIds(machine);
        var artifactsToAssign = assignedArtifacts
            .Except(machine.LoadedArtifacts)
            .Except(machine.PendingArtifacts.Keys)
            .ToList();
        var artifactsToUnassign = machine.LoadedArtifacts
            .Concat(machine.PendingArtifacts.Keys)
            .Except(assignedArtifacts).ToList();

        if (logger.IsEnabled(LogLevel.Debug) && (artifactsToAssign.Count > 0 || artifactsToUnassign.Count > 0))
        {
            logger.LogDebug("Processing artifact assignment for machine {MachineId}. Assigned artifacts: {AssignedArtifacts}. Loaded artifacts: {LoadedArtifacts}. Pending artifacts: {PendingArtifacts}. Artifacts to assign: {ArtifactsToAssign}. Artifacts to unassign: {ArtifactsToUnassign}.", 
                        machine.Id, assignedArtifacts, machine.LoadedArtifacts, machine.PendingArtifacts.Keys, artifactsToAssign, artifactsToUnassign);
        }
        
        foreach (var artifactId in artifactsToUnassign)
        {
            await mqttService.Value.PublishAsync(new ArtifactUnassignmentMessage
            {
                ArtifactId = artifactId
            }, MqttTopicContext.CreateCommandForMachine(machine.Id, ArtifactUnassignmentMessage.MessageType));
            logger.LogInformation("Unassigned artifact {ArtifactId} from machine {MachineId}.", artifactId, machine.Id);
        }

        foreach (var artifactId in artifactsToAssign)
        {
            var artifact = artifactRegistry.GetArtifactByIdAsync(artifactId).Result;
            var job = new TorrentJob()
            {
                Name = artifact.Name,
                ArtifactId = artifact.ID,
                TorrentFileUrl = artifact.TorrentFileUrl,
                DestinationSelector = null,
                SavePath = null
            };
            await mqttService.Value.PublishAsync(new ArtifactAssignmentMessage()
            {
                TorrentJob = job
            }, MqttTopicContext.CreateCommandForMachine(machine.Id, ArtifactAssignmentMessage.MessageType));
            logger.LogInformation("Assigned artifact {ArtifactId} to machine {MachineId}.", artifactId, machine.Id);
        }
    }
    
    private List<string> GetAllAssignedArtifactIds(Machine machine)
    {
        return dbContext.Zones.Local
            .Where(zone => zone.Contains(machine))
            .SelectMany(zone => zone.AssignedArtifactIds)
            .Distinct()
            .ToList();
    }
}