using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace btserver.Data.Migrations;

[DbContext(typeof(BtDbContext))]
[Migration("20260930120000_AddDistributionZoneRole")]
public partial class AddDistributionZoneRole : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsDistributionZone",
            table: "StaticZones",
            type: "INTEGER",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsDistributionZone",
            table: "SubnetZones",
            type: "INTEGER",
            nullable: false,
            defaultValue: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "IsDistributionZone", table: "StaticZones");
        migrationBuilder.DropColumn(name: "IsDistributionZone", table: "SubnetZones");
    }
}
