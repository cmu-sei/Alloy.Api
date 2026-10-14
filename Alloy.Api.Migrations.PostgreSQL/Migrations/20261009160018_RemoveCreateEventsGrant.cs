using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alloy.Api.Migrations.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCreateEventsGrant : Migration
    {
        // SystemPermission.CreateEvents. Events are now created only by launching a template,
        // so the permission grants nothing. The enum member stays so stored values remain valid.
        private const int CreateEvents = 4;
        private const string ContentDeveloperRoleId = "d80b73c3-95d7-4468-8650-c62bbd082507";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF scaffolds an UpdateData that replaces the whole array of the seeded role, which would
            // erase grants an administrator added. Remove only the one value, from every role.
            migrationBuilder.Sql($"UPDATE system_roles SET permissions = array_remove(permissions, {CreateEvents});");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the seeded grant only; which custom roles held it is not recorded.
            migrationBuilder.Sql($"""
                UPDATE system_roles SET permissions = array_append(permissions, {CreateEvents})
                WHERE id = '{ContentDeveloperRoleId}' AND NOT ({CreateEvents} = ANY(permissions));
                """);
        }
    }
}
