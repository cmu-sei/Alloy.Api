// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alloy.Api.Migrations.PostgreSQL.Migrations
{
    public partial class nounChange : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "definitions",
                newName: "event_templates");

            migrationBuilder.RenameColumn(
                name: "scenario_id",
                table: "event_templates",
                newName: "scenario_template_id");

            migrationBuilder.RenameTable(
                name: "implementations",
                newName: "events");

            migrationBuilder.RenameColumn(
                name: "definition_id",
                table: "events",
                newName: "event_template_id");

            migrationBuilder.RenameColumn(
                name: "session_id",
                table: "events",
                newName: "scenario_id");

        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "scenario_id",
                table: "events",
                newName: "session_id");

            migrationBuilder.RenameColumn(
                name: "event_template_id",
                table: "events",
                newName: "definition_id");

            migrationBuilder.RenameTable(
                name: "events",
                newName: "implementations");

            migrationBuilder.RenameColumn(
                name: "scenario_template_id",
                table: "event_templates",
                newName: "scenario_id");

            migrationBuilder.RenameTable(
                name: "event_templates",
                newName: "definitions");

        }
    }
}
