using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace btserver.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddZonePolicyConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubnetZonePolicyConfigurations",
                columns: table => new
                {
                    ZoneId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProxyCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ProxyMachineIdsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubnetZonePolicyConfigurations", x => x.ZoneId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubnetZonePolicyConfigurations");
        }
    }
}
