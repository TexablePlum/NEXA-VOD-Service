using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexa.DrmLicenseServer.Migrations
{
    /// <inheritdoc />
    public partial class AddLastHeartbeat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_heartbeat",
                table: "issued_licenses",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_heartbeat",
                table: "issued_licenses");
        }
    }
}
