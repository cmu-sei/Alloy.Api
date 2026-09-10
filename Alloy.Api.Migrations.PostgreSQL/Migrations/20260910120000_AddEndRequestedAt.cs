using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alloy.Api.Migrations.PostgreSQL.Migrations
{
    public partial class AddEndRequestedAt : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "end_requested_at",
                table: "events",
                type: "timestamp with time zone",
                nullable: true);

            // Old EndDate values recorded intent. Keep historical completion dates,
            // but let unfinished events record their actual cleanup completion later.
            migrationBuilder.Sql("""
                UPDATE events SET end_requested_at = end_date WHERE end_date IS NOT NULL;
                UPDATE events SET end_date = NULL
                WHERE end_date IS NOT NULL AND status NOT IN (4, 5)
                  AND (status <> 10 OR workspace_id IS NOT NULL OR view_id IS NOT NULL OR scenario_id IS NOT NULL);
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Older workers recover Ending events, but do not recover pending Active/Paused requests.
            // Translate live requests before restoring EndDate; leave existing cleanup progress intact.
            migrationBuilder.Sql("""
                UPDATE events SET status = 11, internal_status = 21
                WHERE end_date IS NULL AND end_requested_at IS NOT NULL
                  AND status IN (1, 2, 3, 6, 8);
                UPDATE events SET end_date = end_requested_at
                WHERE end_date IS NULL AND end_requested_at IS NOT NULL;
                """);
            migrationBuilder.DropColumn(name: "end_requested_at", table: "events");
        }
    }
}
