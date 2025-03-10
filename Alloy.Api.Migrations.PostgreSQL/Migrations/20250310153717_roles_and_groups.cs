using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Alloy.Api.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class roles_and_groups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    name = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    all_permissions = table.Column<bool>(type: "boolean", nullable: false),
                    permissions = table.Column<int[]>(type: "integer[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "event_template_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    name = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    all_permissions = table.Column<bool>(type: "boolean", nullable: false),
                    permissions = table.Column<int[]>(type: "integer[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_template_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    name = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    key = table.Column<string>(type: "text", nullable: true),
                    value = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    read_only = table.Column<bool>(type: "boolean", nullable: false),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    name = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    all_permissions = table.Column<bool>(type: "boolean", nullable: false),
                    immutable = table.Column<bool>(type: "boolean", nullable: false),
                    permissions = table.Column<int[]>(type: "integer[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    name = table.Column<string>(type: "text", nullable: true),
                    role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    date_created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    date_modified = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    modified_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.ForeignKey(
                        name: "FK_users_system_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "system_roles",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "event_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("f870d8ee-7332-4f7f-8ee0-63bd07cfd7e4"))
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_memberships", x => x.id);
                    table.ForeignKey(
                        name: "FK_event_memberships_event_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "event_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_event_memberships_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_event_memberships_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_event_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "event_template_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    event_template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("f870d8ee-7332-4f7f-8ee0-63bd07cfd7e4"))
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_template_memberships", x => x.id);
                    table.ForeignKey(
                        name: "FK_event_template_memberships_event_template_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "event_template_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_event_template_memberships_event_templates_event_template_id",
                        column: x => x.event_template_id,
                        principalTable: "event_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_event_template_memberships_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_event_template_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "group_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_memberships", x => x.id);
                    table.ForeignKey(
                        name: "FK_group_memberships_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_group_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "event_roles",
                columns: new[] { "id", "all_permissions", "description", "name", "permissions" },
                values: new object[,]
                {
                    { new Guid("1a3f26cd-9d99-4b98-b914-12931e786198"), true, "Can perform all actions on the Event", "Manager", new int[0] },
                    { new Guid("39aa296e-05ba-4fb0-8d74-c92cf3354c6f"), false, "Has read only access to the Event", "Observer", new[] { 0 } },
                    { new Guid("f870d8ee-7332-4f7f-8ee0-63bd07cfd7e4"), false, "Has read only access to the Event", "Member", new[] { 0, 1 } }
                });

            migrationBuilder.InsertData(
                table: "event_template_roles",
                columns: new[] { "id", "all_permissions", "description", "name", "permissions" },
                values: new object[,]
                {
                    { new Guid("1a3f26cd-9d99-4b98-b914-12931e786198"), true, "Can perform all actions on the EventTemplate", "Manager", new int[0] },
                    { new Guid("39aa296e-05ba-4fb0-8d74-c92cf3354c6f"), false, "Has read only access to the EventTemplate", "Observer", new[] { 0 } },
                    { new Guid("f870d8ee-7332-4f7f-8ee0-63bd07cfd7e4"), false, "Has read only access to the EventTemplate", "Member", new[] { 0, 1 } }
                });

            migrationBuilder.InsertData(
                table: "system_roles",
                columns: new[] { "id", "all_permissions", "description", "immutable", "name", "permissions" },
                values: new object[,]
                {
                    { new Guid("1da3027e-725d-4753-9455-a836ed9bdb1e"), false, "Can View all Event Templates and Events, but cannot make any changes.", false, "Observer", new[] { 1, 5, 9, 11, 13 } },
                    { new Guid("d80b73c3-95d7-4468-8650-c62bbd082507"), false, "Can create and manage their own Event Templates and Events.", false, "Content Developer", new[] { 0, 4, 7 } },
                    { new Guid("f35e8fff-f996-4cba-b303-3ba515ad8d2f"), true, "Can perform all actions", true, "Administrator", new int[0] }
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_memberships_event_id_user_id_group_id",
                table: "event_memberships",
                columns: new[] { "event_id", "user_id", "group_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_memberships_group_id",
                table: "event_memberships",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_event_memberships_role_id",
                table: "event_memberships",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_event_memberships_user_id",
                table: "event_memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_event_template_memberships_event_template_id_user_id_group_~",
                table: "event_template_memberships",
                columns: new[] { "event_template_id", "user_id", "group_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_template_memberships_group_id",
                table: "event_template_memberships",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_event_template_memberships_role_id",
                table: "event_template_memberships",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_event_template_memberships_user_id",
                table: "event_template_memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_group_memberships_group_id_user_id",
                table: "group_memberships",
                columns: new[] { "group_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_group_memberships_user_id",
                table: "group_memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_permissions_key_value",
                table: "permissions",
                columns: new[] { "key", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_roles_name",
                table: "system_roles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_id",
                table: "users",
                column: "id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_role_id",
                table: "users",
                column: "role_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_memberships");

            migrationBuilder.DropTable(
                name: "event_template_memberships");

            migrationBuilder.DropTable(
                name: "group_memberships");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "event_roles");

            migrationBuilder.DropTable(
                name: "event_template_roles");

            migrationBuilder.DropTable(
                name: "groups");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "system_roles");
        }
    }
}
