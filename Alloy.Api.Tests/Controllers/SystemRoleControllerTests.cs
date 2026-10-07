// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Tests.Support;
using Alloy.Api.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Alloy.Api.Tests.Controllers;

/// <summary><c>SystemRoleController</c> over HTTP: ViewRoles to read, ManageRoles to write.</summary>
public class SystemRoleControllerTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task GetAll_returns_the_seeded_roles_to_a_caller_holding_ViewRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        var roles = await ReadAsync<List<SystemRole>>(await Client(actor).GetAsync("api/system-roles", Ct));

        Assert.Contains(TestData.Roles.Administrator, roles.Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_holding_only_ManageUsers()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageUsers, SystemPermission.ViewUsers).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/system-roles", Ct));
    }

    [Fact]
    public async Task Get_returns_the_role_with_its_permissions_to_a_caller_holding_ViewRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        var role = await ReadAsync<SystemRole>(await Client(actor).GetAsync($"api/system-roles/{TestData.Roles.ContentDeveloper}", Ct));

        Assert.Equal(
            [SystemPermission.CreateEventTemplates, SystemPermission.CreateEvents, SystemPermission.ExecuteEvents],
            role.Permissions.Order());
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_ViewUsers()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/system-roles/{TestData.Roles.Observer}", Ct));
    }

    /// <summary>A role that does not exist is answered with a 204 and no body.</summary>
    [Fact]
    public async Task Get_answers_an_unknown_role_with_no_content()
    {
        await AssertStatus(HttpStatusCode.NoContent, await RootClient.GetAsync($"api/system-roles/{Guid.NewGuid()}", Ct));
    }

    [Fact]
    public async Task Create_by_a_caller_holding_ManageRoles_stores_the_role()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageRoles).SeedAsync();

        var created = await ReadAsync<SystemRole>(await Client(actor).PostAsJsonAsync("api/system-roles",
            new SystemRole { Name = "Range Tech", Permissions = [SystemPermission.ViewEvents] }, AlloyJson, Ct));

        var saved = await StoredRole(created.Id);
        Assert.Equal(("Range Tech", false), (saved.Name, saved.AllPermissions));
        Assert.Equal([SystemPermission.ViewEvents], saved.Permissions);
    }

    /// <summary>A name another role holds is refused by the unique index on <c>system_roles.name</c>, answered with a 500.</summary>
    [Fact]
    public async Task Create_answers_a_name_that_is_already_taken_with_a_server_error()
    {
        var response = await RootClient.PostAsJsonAsync("api/system-roles", new SystemRole { Name = "Administrator", Permissions = [] }, AlloyJson, Ct);

        var problem = await AssertJsonError(HttpStatusCode.InternalServerError, response);
        Assert.Equal("An error occurred while saving the entity changes. See the inner exception for details.", problem.Detail);
        await using var context = NewContext();
        Assert.Equal(1, await context.SystemRoles.CountAsync(x => x.Name == "Administrator", Ct));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles, SystemPermission.ManageUsers).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/system-roles", new SystemRole { Name = "Denied", Permissions = [] }, AlloyJson, Ct);

        await AssertJsonError(HttpStatusCode.Forbidden, response);
        await using var context = NewContext();
        Assert.False(await context.SystemRoles.AnyAsync(x => x.Name == "Denied", Ct));
    }

    [Fact]
    public async Task Update_by_a_caller_holding_ManageRoles_stores_the_permissions()
    {
        var role = TestData.SystemRole(permissions: [SystemPermission.ViewEvents]);
        await Seed(role);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageRoles).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/system-roles/{role.Id}",
            new SystemRole { Id = role.Id, Name = role.Name, Permissions = [SystemPermission.ViewEvents, SystemPermission.EditEvents] }, AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal([SystemPermission.ViewEvents, SystemPermission.EditEvents], (await StoredRole(role.Id)).Permissions);
    }

    /// <summary>The seeded Administrator role is marked immutable, and an update of it is stored anyway.</summary>
    [Fact]
    public async Task Update_stores_a_change_to_the_immutable_administrator_role()
    {
        var response = await RootClient.PutAsJsonAsync($"api/system-roles/{TestData.Roles.Administrator}",
            new SystemRole { Id = TestData.Roles.Administrator, Name = "Administrator", AllPermissions = false, Immutable = true, Permissions = [] }, AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.False((await StoredRole(TestData.Roles.Administrator)).AllPermissions);
    }

    /// <summary>The body's id is mapped onto the stored row, so an id other than the route's is answered with a 500.</summary>
    [Fact]
    public async Task Update_answers_a_body_id_other_than_the_routes_with_a_server_error()
    {
        var role = TestData.SystemRole();
        await Seed(role);

        var response = await RootClient.PutAsJsonAsync($"api/system-roles/{role.Id}",
            new SystemRole { Id = Guid.NewGuid(), Name = "Renamed", Permissions = [] }, AlloyJson, Ct);

        var problem = await AssertJsonError(HttpStatusCode.InternalServerError, response);
        Assert.StartsWith("The property 'SystemRoleEntity.Id' is part of a key", problem.Detail);
        Assert.Equal(role.Name, (await StoredRole(role.Id)).Name);
    }

    [Fact]
    public async Task Update_is_forbidden_for_a_caller_holding_only_ViewRoles()
    {
        var role = TestData.SystemRole();
        await Seed(role);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/system-roles/{role.Id}",
            new SystemRole { Id = role.Id, Name = role.Name, AllPermissions = true, Permissions = [] }, AlloyJson, Ct);

        await AssertJsonError(HttpStatusCode.Forbidden, response);
        Assert.False((await StoredRole(role.Id)).AllPermissions);
    }

    [Fact]
    public async Task Delete_by_a_caller_holding_ManageRoles_removes_the_role()
    {
        var role = TestData.SystemRole();
        await Seed(role);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageRoles).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/system-roles/{role.Id}", Ct));

        await using var context = NewContext();
        Assert.False(await context.SystemRoles.AnyAsync(x => x.Id == role.Id, Ct));
    }

    // Same case as Update_stores_a_change_to_the_immutable_administrator_role.
    [Fact]
    public async Task Delete_removes_a_role_marked_immutable()
    {
        var role = TestData.SystemRole(immutable: true);
        await Seed(role);

        await AssertStatus(HttpStatusCode.NoContent, await RootClient.DeleteAsync($"api/system-roles/{role.Id}", Ct));

        await using var context = NewContext();
        Assert.False(await context.SystemRoles.AnyAsync(x => x.Id == role.Id, Ct));
    }

    /// <summary>A role a user holds is refused by the foreign key from <c>users.role_id</c>, answered with a 500.</summary>
    [Fact]
    public async Task Delete_answers_a_role_held_by_a_user_with_a_server_error()
    {
        var role = TestData.SystemRole();
        await Seed(role, TestData.User(roleId: role.Id));

        var problem = await AssertJsonError(HttpStatusCode.InternalServerError, await RootClient.DeleteAsync($"api/system-roles/{role.Id}", Ct));
        Assert.Equal("An error occurred while saving the entity changes. See the inner exception for details.", problem.Detail);

        await using var context = NewContext();
        Assert.True(await context.SystemRoles.AnyAsync(x => x.Id == role.Id, Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ManageUsers()
    {
        var role = TestData.SystemRole();
        await Seed(role);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageUsers, SystemPermission.ViewRoles).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).DeleteAsync($"api/system-roles/{role.Id}", Ct));
    }

    private async Task<SystemRoleEntity> StoredRole(Guid id)
    {
        await using var context = NewContext();

        return await context.SystemRoles.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }
}
