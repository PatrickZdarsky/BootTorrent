using btserver.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace btserver.Tests;

public class ProxyMigrationTests
{
    [Fact]
    public async Task ExistingSubnetProxyPreferencesBecomeZoneConfiguration()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<BtDbContext>().UseSqlite(connection).Options;
        await using var db = new BtDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260930120000_AddDistributionZoneRole");
        var zoneId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO SubnetZonePolicyConfigurations (ZoneId, ProxyCount, ProxyMachineIdsJson) VALUES ({zoneId}, {2}, {"[\"p0\",\"p1\"]"})");

        await migrator.MigrateAsync();

        var configuration = await db.ProxyZoneConfigurations.SingleAsync();
        Assert.Equal(zoneId, configuration.ZoneId);
        Assert.Equal(2, configuration.DesiredInitialCount);
        Assert.Equal("stable-id", configuration.SelectionPolicyKey);
        Assert.Equal(["p0", "p1"], configuration.PreferredMachineIds);
    }
}
