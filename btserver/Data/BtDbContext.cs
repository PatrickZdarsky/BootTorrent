using btserver.Zone;
using Microsoft.EntityFrameworkCore;

namespace btserver.Data;

public class BtDbContext(DbContextOptions<BtDbContext> options) : DbContext(options)
{
    public DbSet<Zone.Zone> Zones { get; set; }
    public DbSet<StaticZone> StaticZones { get; set; }
    public DbSet<SubnetZone> SubnetZones { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Zone.Zone>().UseTpcMappingStrategy();
    }
}
