using btserver.Zone;
using Microsoft.EntityFrameworkCore;

namespace btserver.Data;

public class BtDbContext(DbContextOptions<BtDbContext> options) : DbContext(options)
{
    public DbSet<Zone.Zone> Zones { get; set; }
    public DbSet<StaticZone> StaticZones { get; set; }
    public DbSet<SubnetZone> SubnetZones { get; set; }
    public DbSet<ProxyZoneConfiguration> ProxyZoneConfigurations { get; set; }
    public DbSet<ProxyElectionState> ProxyElectionStates { get; set; }
    public DbSet<ProxyDesignation> ProxyDesignations { get; set; }
    public DbSet<ProxyElectionTransition> ProxyElectionTransitions { get; set; }
    public DbSet<DefaultZoneArtifactAssignment> DefaultZoneArtifactAssignments { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Zone.Zone>().UseTpcMappingStrategy();
        modelBuilder.Entity<ProxyZoneConfiguration>()
            .Property(configuration => configuration.PreferredMachineIdsJson)
            .HasDefaultValue("[]");
        modelBuilder.Entity<ProxyElectionState>().HasKey(state => new { state.ZoneId, state.ArtifactId });
        modelBuilder.Entity<ProxyDesignation>().HasKey(designation => new { designation.ZoneId, designation.ArtifactId, designation.MachineId });
        modelBuilder.Entity<ProxyElectionTransition>()
            .HasIndex(transition => new { transition.ZoneId, transition.ArtifactId, transition.Id });
        modelBuilder.Entity<DefaultZoneArtifactAssignment>().HasKey(assignment => assignment.ArtifactId);
    }
}
