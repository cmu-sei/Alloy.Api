// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alloy.Api.Tests.Data;

/// <summary>
/// The <c>RemoveCreateEventsGrant</c> migration (Alloy.Api.Migrations.PostgreSQL), run down and up on a clone
/// of the migrated template: it removes only <c>CreateEvents</c>, from every role, so grants an administrator
/// added survive; rolled back, it restores the seeded grant to Content Developer and to no other role.
/// </summary>
public class RemoveCreateEventsGrantMigrationTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    /// <summary>The migration before <c>RemoveCreateEventsGrant</c>.</summary>
    private const string PreviousMigration = "20260910120000_AddEndRequestedAt";

    /// <summary>A role an administrator made, holding <c>CreateEvents</c> and <c>ViewEvents</c>.</summary>
    private static readonly Guid CustomRoleId = Guid.Parse("4a3c9f0e-7d2b-4e61-9a85-1f6b2c0d3e47");

    /// <summary>
    /// What an administrator did under the previous release: added <c>ManageUsers</c> to Content Developer
    /// and made a custom role holding <c>CreateEvents</c>.
    /// </summary>
    private static readonly string AdministratorEdits = $"""
        UPDATE system_roles SET permissions = array_append(permissions, {(int)SystemPermission.ManageUsers})
        WHERE id = '{SystemRoleEntityDefaults.ContentDeveloperRoleId}';
        INSERT INTO system_roles (id, name, all_permissions, immutable, permissions, description)
        VALUES ('{CustomRoleId}', 'Custom', false, false,
            ARRAY[{(int)SystemPermission.CreateEvents}, {(int)SystemPermission.ViewEvents}], '');
        """;

    [Fact]
    public async Task Upgrading_removes_only_CreateEvents_from_every_role()
    {
        await RollBackAndApplyAdministratorEdits();

        await Migrator().MigrateAsync(cancellationToken: Ct);

        Assert.Equal(
            [SystemPermission.CreateEventTemplates, SystemPermission.ExecuteEvents, SystemPermission.ManageUsers],
            await Permissions(SystemRoleEntityDefaults.ContentDeveloperRoleId));
        Assert.Equal([SystemPermission.ViewEvents], await Permissions(CustomRoleId));
    }

    /// <summary>Which custom roles held <c>CreateEvents</c> is not recorded, so only the seeded grant comes back.</summary>
    [Fact]
    public async Task Rolling_back_restores_CreateEvents_to_Content_Developer_only()
    {
        await RollBackAndApplyAdministratorEdits();
        await Migrator().MigrateAsync(cancellationToken: Ct);

        await Migrator().MigrateAsync(PreviousMigration, Ct);

        var contentDeveloper = await Permissions(SystemRoleEntityDefaults.ContentDeveloperRoleId);
        Assert.Contains(SystemPermission.CreateEvents, contentDeveloper);
        Assert.Contains(SystemPermission.ManageUsers, contentDeveloper);
        Assert.DoesNotContain(SystemPermission.CreateEvents, await Permissions(CustomRoleId));
    }

    private IMigrator Migrator() => Db.GetService<IMigrator>();

    private async Task RollBackAndApplyAdministratorEdits()
    {
        await Migrator().MigrateAsync(PreviousMigration, Ct);
        await Db.Database.ExecuteSqlRawAsync(AdministratorEdits, Ct);
    }

    private async Task<SystemPermission[]> Permissions(Guid roleId)
    {
        await using var context = NewContext();
        var role = await context.SystemRoles.AsNoTracking().SingleAsync(x => x.Id == roleId, Ct);

        return [.. role.Permissions.Order()];
    }
}
