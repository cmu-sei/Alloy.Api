using System;
using System.Linq;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Alloy.Api.Tests;

/// <summary>
/// The migration removes only CreateEvents, so grants an administrator added to a role survive.
/// </summary>
[Collection("Postgres")]
public class RemoveCreateEventsGrantMigrationTests(PostgresFixture postgres)
{
    private const string Previous = "20260910120000_AddEndRequestedAt";
    private static readonly Guid CustomRoleId = Guid.NewGuid();

    [Fact]
    public async Task UpgradeRemovesOnlyCreateEventsAndRollbackRestoresTheSeededGrant()
    {
        using var database = postgres.CreateDatabase();
        using var db = new AlloyContext(new DbContextOptionsBuilder<AlloyContext>()
            .UseNpgsql(database.ConnectionString, x => x.MigrationsAssembly("Alloy.Api.Migrations.PostgreSQL")).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous);
        // An administrator added ManageUsers to Content Developer and made a custom role with CreateEvents.
        await db.Database.ExecuteSqlRawAsync($"""
            UPDATE system_roles SET permissions = array_append(permissions, {(int)SystemPermission.ManageUsers})
            WHERE id = '{SystemRoleEntityDefaults.ContentDeveloperRoleId}';
            INSERT INTO system_roles (id, name, all_permissions, immutable, permissions, description)
            VALUES ('{CustomRoleId}', 'Custom', false, false,
                ARRAY[{(int)SystemPermission.CreateEvents}, {(int)SystemPermission.ViewEvents}], '');
            """);

        await migrator.MigrateAsync();

        Assert.Equal([SystemPermission.CreateEventTemplates, SystemPermission.ExecuteEvents, SystemPermission.ManageUsers],
            await Permissions(db, SystemRoleEntityDefaults.ContentDeveloperRoleId));
        Assert.Equal([SystemPermission.ViewEvents], await Permissions(db, CustomRoleId));

        await migrator.MigrateAsync(Previous);

        Assert.Contains(SystemPermission.CreateEvents, await Permissions(db, SystemRoleEntityDefaults.ContentDeveloperRoleId));
        Assert.Contains(SystemPermission.ManageUsers, await Permissions(db, SystemRoleEntityDefaults.ContentDeveloperRoleId));
        Assert.DoesNotContain(SystemPermission.CreateEvents, await Permissions(db, CustomRoleId));
    }

    private static async Task<SystemPermission[]> Permissions(AlloyContext db, Guid roleId)
    {
        db.ChangeTracker.Clear();
        var role = await db.SystemRoles.AsNoTracking().SingleAsync(x => x.Id == roleId);
        return role.Permissions.OrderBy(x => x).ToArray();
    }
}
