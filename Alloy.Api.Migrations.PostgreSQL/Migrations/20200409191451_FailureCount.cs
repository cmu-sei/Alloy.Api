// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alloy.Api.Migrations.PostgreSQL.Migrations
{
    public partial class FailureCount : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "failure_count",
                table: "implementations",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "last_end_internal_status",
                table: "implementations",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "last_end_status",
                table: "implementations",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "last_launch_internal_status",
                table: "implementations",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "last_launch_status",
                table: "implementations",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "failure_count",
                table: "implementations");

            migrationBuilder.DropColumn(
                name: "last_end_internal_status",
                table: "implementations");

            migrationBuilder.DropColumn(
                name: "last_end_status",
                table: "implementations");

            migrationBuilder.DropColumn(
                name: "last_launch_internal_status",
                table: "implementations");

            migrationBuilder.DropColumn(
                name: "last_launch_status",
                table: "implementations");
        }
    }
}
