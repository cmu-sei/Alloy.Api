// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore.Migrations;

namespace Alloy.Api.Migrations.PostgreSQL.Migrations
{
    public partial class renameIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.RenameIndex(
                name: "IX_implementations_definition_id",
                table: "events",
                newName: "IX_events_event_template_id");

            migrationBuilder.DropForeignKey(
                name: "FK_implementations_definitions_definition_id",
                table: "events");

            migrationBuilder.DropPrimaryKey(
                name: "PK_implementations",
                table: "events");

            migrationBuilder.DropPrimaryKey(
                name: "PK_definitions",
                table: "event_templates");

            migrationBuilder.AddPrimaryKey(
                name: "PK_event_templates",
                table: "event_templates",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_events",
                table: "events",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_events_event_templates_event_template_id",
                table: "events",
                column: "event_template_id",
                principalTable: "event_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.RenameIndex(
                newName: "IX_implementations_definition_id",
                table: "events",
                name: "IX_events_event_template_id");

            migrationBuilder.DropForeignKey(
                name: "FK_events_event_templates_event_template_id",
                table: "events");

            migrationBuilder.DropPrimaryKey(
                name: "PK_events",
                table: "events");

            migrationBuilder.DropPrimaryKey(
                name: "PK_event_templates",
                table: "event_templates");

            migrationBuilder.AddPrimaryKey(
                name: "PK_definitions",
                table: "event_templates",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_implementations",
                table: "events",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_implementations_definitions_definition_id",
                table: "events",
                column: "event_template_id",
                principalTable: "event_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

        }
    }
}
