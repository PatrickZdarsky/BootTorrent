using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace btserver.Data.Migrations;

[DbContext(typeof(BtDbContext))]
[Migration("20260930130000_AddProxyElection")]
public partial class AddProxyElection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProxyZoneConfigurations",
            columns: table => new
            {
                ZoneId = table.Column<Guid>(type: "TEXT", nullable: false),
                DesiredInitialCount = table.Column<int>(type: "INTEGER", nullable: false),
                SelectionPolicyKey = table.Column<string>(type: "TEXT", nullable: false),
                PreferredMachineIdsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]")
            },
            constraints: table => table.PrimaryKey("PK_ProxyZoneConfigurations", row => row.ZoneId));

        migrationBuilder.Sql("""
            INSERT INTO ProxyZoneConfigurations (ZoneId, DesiredInitialCount, SelectionPolicyKey, PreferredMachineIdsJson)
            SELECT ZoneId, ProxyCount, 'stable-id', ProxyMachineIdsJson
            FROM SubnetZonePolicyConfigurations;
            """);

        migrationBuilder.CreateTable(
            name: "ProxyElectionStates",
            columns: table => new
            {
                ZoneId = table.Column<Guid>(type: "TEXT", nullable: false),
                ArtifactId = table.Column<string>(type: "TEXT", nullable: false),
                AppliedInitialCount = table.Column<int>(type: "INTEGER", nullable: false),
                Initialized = table.Column<bool>(type: "INTEGER", nullable: false),
                Generation = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ProxyElectionStates", row => new { row.ZoneId, row.ArtifactId }));

        migrationBuilder.CreateTable(
            name: "ProxyDesignations",
            columns: table => new
            {
                ZoneId = table.Column<Guid>(type: "TEXT", nullable: false),
                ArtifactId = table.Column<string>(type: "TEXT", nullable: false),
                MachineId = table.Column<string>(type: "TEXT", nullable: false),
                SelectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                Reason = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ProxyDesignations", row => new { row.ZoneId, row.ArtifactId, row.MachineId }));

        migrationBuilder.CreateTable(
            name: "ProxyElectionTransitions",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                ZoneId = table.Column<Guid>(type: "TEXT", nullable: false),
                ArtifactId = table.Column<string>(type: "TEXT", nullable: false),
                MachineId = table.Column<string>(type: "TEXT", nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                Action = table.Column<string>(type: "TEXT", nullable: false),
                Reason = table.Column<string>(type: "TEXT", nullable: false),
                Generation = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ProxyElectionTransitions", row => row.Id));
        migrationBuilder.CreateIndex(
            name: "IX_ProxyElectionTransitions_ZoneId_ArtifactId_Id",
            table: "ProxyElectionTransitions",
            columns: new[] { "ZoneId", "ArtifactId", "Id" });

        migrationBuilder.CreateTable(
            name: "DefaultZoneArtifactAssignments",
            columns: table => new { ArtifactId = table.Column<string>(type: "TEXT", nullable: false) },
            constraints: table => table.PrimaryKey("PK_DefaultZoneArtifactAssignments", row => row.ArtifactId));

        migrationBuilder.DropTable(name: "SubnetZonePolicyConfigurations");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SubnetZonePolicyConfigurations",
            columns: table => new
            {
                ZoneId = table.Column<Guid>(type: "TEXT", nullable: false),
                ProxyCount = table.Column<int>(type: "INTEGER", nullable: false),
                ProxyMachineIdsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]")
            },
            constraints: table => table.PrimaryKey("PK_SubnetZonePolicyConfigurations", row => row.ZoneId));
        migrationBuilder.Sql("""
            INSERT INTO SubnetZonePolicyConfigurations (ZoneId, ProxyCount, ProxyMachineIdsJson)
            SELECT ZoneId, DesiredInitialCount, PreferredMachineIdsJson
            FROM ProxyZoneConfigurations WHERE ZoneId IN (SELECT Id FROM SubnetZones);
            """);
        migrationBuilder.DropTable(name: "DefaultZoneArtifactAssignments");
        migrationBuilder.DropTable(name: "ProxyElectionTransitions");
        migrationBuilder.DropTable(name: "ProxyDesignations");
        migrationBuilder.DropTable(name: "ProxyElectionStates");
        migrationBuilder.DropTable(name: "ProxyZoneConfigurations");
    }
}
