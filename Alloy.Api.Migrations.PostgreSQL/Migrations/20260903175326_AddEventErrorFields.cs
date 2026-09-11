using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alloy.Api.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddEventErrorFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "error_detail",
                table: "events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "error_message",
                table: "events",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "error_detail",
                table: "events");

            migrationBuilder.DropColumn(
                name: "error_message",
                table: "events");
        }
    }
}
