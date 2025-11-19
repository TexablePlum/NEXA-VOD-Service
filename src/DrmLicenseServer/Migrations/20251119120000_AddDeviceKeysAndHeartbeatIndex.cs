using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nexa.DrmLicenseServer.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceKeysAndHeartbeatIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_issued_licenses_UserId_LastHeartbeat",
                table: "issued_licenses",
                columns: new[] { "user_id", "last_heartbeat" });

            migrationBuilder.CreateTable(
                name: "user_device_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    device_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    public_key_pem = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    tpm_attestation = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    registered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_device_keys", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_device_keys_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_device_keys_UserId_DeviceId",
                table: "user_device_keys",
                columns: new[] { "user_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_device_keys_DeviceId",
                table: "user_device_keys",
                column: "device_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_device_keys");

            migrationBuilder.DropIndex(
                name: "IX_issued_licenses_UserId_LastHeartbeat",
                table: "issued_licenses");
        }
    }
}
