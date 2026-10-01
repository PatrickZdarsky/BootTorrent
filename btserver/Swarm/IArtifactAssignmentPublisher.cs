namespace btserver.Swarm;

/// <summary>Publishes artifact jobs directly to a machine.</summary>
public interface IArtifactAssignmentPublisher
{
    /// <summary>Publishes an artifact assignment to one machine.</summary>
    /// <param name="machineId">The destination machine ID.</param>
    /// <param name="artifactId">The registered artifact ID.</param>
    /// <param name="cancellationToken">Cancels publication.</param>
    /// <returns>A task for the publication.</returns>
    Task PublishAssignmentToMachineAsync(string machineId, string artifactId, CancellationToken cancellationToken);

    /// <summary>Publishes an artifact unassignment to one machine.</summary>
    /// <param name="machineId">The destination machine ID.</param>
    /// <param name="artifactId">The artifact ID.</param>
    /// <param name="cancellationToken">Cancels publication.</param>
    /// <returns>A task for the publication.</returns>
    Task PublishUnassignmentToMachineAsync(string machineId, string artifactId, CancellationToken cancellationToken);
}
