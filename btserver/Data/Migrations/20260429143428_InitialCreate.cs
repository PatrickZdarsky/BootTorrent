using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace btserver.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StaticZones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    AssignedArtifactIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    MachineIdsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaticZones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubnetZones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    AssignedArtifactIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Subnet = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubnetZones", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StaticZones");

            migrationBuilder.DropTable(
                name: "SubnetZones");
        }
    }
}
