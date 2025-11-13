using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alloy.Api.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class VersionUpgradeSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_event_users_events_event_id",
                table: "event_users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_event_users",
                table: "event_users");

            migrationBuilder.RenameTable(
                name: "event_users",
                newName: "event_user_entity");

            migrationBuilder.RenameIndex(
                name: "IX_event_users_event_id_user_id",
                table: "event_user_entity",
                newName: "IX_event_user_entity_event_id_user_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_event_user_entity",
                table: "event_user_entity",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_event_user_entity_events_event_id",
                table: "event_user_entity",
                column: "event_id",
                principalTable: "events",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_event_user_entity_events_event_id",
                table: "event_user_entity");

            migrationBuilder.DropPrimaryKey(
                name: "PK_event_user_entity",
                table: "event_user_entity");

            migrationBuilder.RenameTable(
                name: "event_user_entity",
                newName: "event_users");

            migrationBuilder.RenameIndex(
                name: "IX_event_user_entity_event_id_user_id",
                table: "event_users",
                newName: "IX_event_users_event_id_user_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_event_users",
                table: "event_users",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_event_users_events_event_id",
                table: "event_users",
                column: "event_id",
                principalTable: "events",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
