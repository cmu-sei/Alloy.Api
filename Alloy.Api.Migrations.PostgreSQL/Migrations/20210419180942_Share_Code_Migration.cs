using Microsoft.EntityFrameworkCore.Migrations;

namespace Alloy.Api.Migrations.PostgreSQL.Migrations
{
    public partial class Share_Code_Migration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "share_code",
                table: "events",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "share_code",
                table: "events");
        }
    }
}
