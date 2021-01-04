// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore.Migrations;

namespace Alloy.Api.Migrations.PostgreSQL.Migrations
{
    public partial class NewNouns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "exercise_id",
                table: "events",
                newName: "view_id");

            migrationBuilder.RenameColumn(
                name: "exercise_id",
                table: "event_templates",
                newName: "view_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "view_id",
                table: "events",
                newName: "exercise_id");

            migrationBuilder.RenameColumn(
                name: "view_id",
                table: "event_templates",
                newName: "exercise_id");
        }
    }
}
