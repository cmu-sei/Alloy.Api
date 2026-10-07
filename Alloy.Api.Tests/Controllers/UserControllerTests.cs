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

/// <summary>
/// <c>UserController</c> over HTTP. The list is open to any of three view permissions and to group
/// managers (who pick members from it); everything else needs ViewUsers or ManageUsers.
/// </summary>
public class UserControllerTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    // GET api/users

    [Theory]
    [InlineData(SystemPermission.ViewUsers)]
    [InlineData(SystemPermission.ViewEventTemplates)]
    [InlineData(SystemPermission.ViewEvents)]
    public async Task GetAll_returns_the_users_to_a_caller_holding_one_of_the_view_permissions(SystemPermission permission)
    {
        var user = await SeedUser();
        var actor = await Actor().WithSystemPermissions(permission).SeedAsync();

        var users = await ReadAsync<List<User>>(await Client(actor).GetAsync("api/users", Ct));

        Assert.Contains(user.Id, users.Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_returns_the_users_to_a_group_manager()
    {
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Manager).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync("api/users", Ct));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_holding_only_ViewRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles, SystemPermission.ViewGroups).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/users", Ct));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_plain_member_of_a_group()
    {
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Member).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/users", Ct));
    }

    // GET api/users/{id}

    [Fact]
    public async Task Get_returns_the_user_to_a_caller_holding_ViewUsers()
    {
        var user = await SeedUser();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();

        var read = await ReadAsync<User>(await Client(actor).GetAsync($"api/users/{user.Id}", Ct));

        Assert.Equal(user.Name, read.Name);
    }

    /// <summary>ViewEvents opens the list but not a single user.</summary>
    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_ViewEvents()
    {
        var user = await SeedUser();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEvents).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/users/{user.Id}", Ct));
    }

    [Fact]
    public async Task Get_of_an_unknown_user_is_not_found()
    {
        await AssertJsonError(HttpStatusCode.NotFound, await RootClient.GetAsync($"api/users/{Guid.NewGuid()}", Ct));
    }

    // POST api/users

    [Fact]
    public async Task Create_by_a_caller_holding_ManageUsers_stores_the_user_with_its_role()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageUsers).SeedAsync();
        var id = Guid.NewGuid();

        var response = await Client(actor).PostAsJsonAsync("api/users",
            new User { Id = id, Name = "Created", RoleId = TestData.Roles.Observer.ToString() }, AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        var saved = await StoredUser(id);
        Assert.Equal(("Created", TestData.Roles.Observer, actor.Id), (saved.Name, saved.RoleId, saved.CreatedBy));
    }

    /// <summary>A user id that already exists is refused by the primary key, answered with a 500.</summary>
    [Fact]
    public async Task Create_answers_an_existing_user_id_with_a_server_error()
    {
        var user = await SeedUser();

        var response = await RootClient.PostAsJsonAsync("api/users", new User { Id = user.Id, Name = "Again" }, AlloyJson, Ct);

        var problem = await AssertJsonError(HttpStatusCode.InternalServerError, response);
        Assert.Equal("An error occurred while saving the entity changes. See the inner exception for details.", problem.Detail);
        Assert.Equal(user.Name, (await StoredUser(user.Id)).Name);
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewUsers()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();
        var id = Guid.NewGuid();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync("api/users", new User { Id = id, Name = "Denied" }, AlloyJson, Ct));

        await using var context = NewContext();
        Assert.False(await context.Users.AnyAsync(x => x.Id == id, Ct));
    }

    // PUT api/Users/{id}

    [Fact]
    public async Task Update_by_a_caller_holding_ManageUsers_changes_the_name_and_role()
    {
        var user = await SeedUser();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageUsers).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/Users/{user.Id}",
            new User { Id = user.Id, Name = "Renamed", RoleId = TestData.Roles.ContentDeveloper.ToString() }, AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var saved = await StoredUser(user.Id);
        Assert.Equal(("Renamed", TestData.Roles.ContentDeveloper), (saved.Name, saved.RoleId));
    }

    [Fact]
    public async Task Update_of_the_callers_own_id_to_another_is_forbidden()
    {
        var response = await RootClient.PutAsJsonAsync($"api/Users/{Root.Id}", new User { Id = Guid.NewGuid(), Name = "Root" }, AlloyJson, Ct);

        var problem = await AssertJsonError(HttpStatusCode.Forbidden, response);
        Assert.Equal("You cannot change your own Id", problem.Title);
    }

    /// <summary>The body's id is mapped onto the stored row, so another user's id changed in the body is answered with a 500.</summary>
    [Fact]
    public async Task Update_answers_a_body_id_other_than_the_routes_with_a_server_error()
    {
        var user = await SeedUser();

        var response = await RootClient.PutAsJsonAsync($"api/Users/{user.Id}", new User { Id = Guid.NewGuid(), Name = "Renamed" }, AlloyJson, Ct);

        var problem = await AssertJsonError(HttpStatusCode.InternalServerError, response);
        Assert.StartsWith("The property 'UserEntity.Id' is part of a key", problem.Detail);
        Assert.Equal(user.Name, (await StoredUser(user.Id)).Name);
    }

    [Fact]
    public async Task Update_is_forbidden_for_a_caller_holding_only_ManageRoles()
    {
        var user = await SeedUser();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageRoles, SystemPermission.ViewUsers).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/Users/{user.Id}",
            new User { Id = user.Id, Name = user.Name, RoleId = TestData.Roles.Administrator.ToString() }, AlloyJson, Ct);

        await AssertJsonError(HttpStatusCode.Forbidden, response);
        Assert.Null((await StoredUser(user.Id)).RoleId);
    }

    // DELETE api/users/{id}

    [Fact]
    public async Task Delete_by_a_caller_holding_ManageUsers_removes_the_user_and_their_group_memberships()
    {
        var user = await SeedUser();
        var group = TestData.Group();
        await Seed(group, TestData.GroupMembership(group.Id, user.Id));
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageUsers).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/users/{user.Id}", Ct));

        await using var context = NewContext();
        Assert.False(await context.Users.AnyAsync(x => x.Id == user.Id, Ct));
        Assert.False(await context.GroupMemberships.AnyAsync(x => x.UserId == user.Id, Ct));
    }

    [Fact]
    public async Task Delete_of_the_callers_own_account_is_forbidden()
    {
        var problem = await AssertJsonError(HttpStatusCode.Forbidden, await RootClient.DeleteAsync($"api/users/{Root.Id}", Ct));

        Assert.Equal("You cannot delete your own account", problem.Title);
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_ViewUsers()
    {
        var user = await SeedUser();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers, SystemPermission.ManageGroups).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).DeleteAsync($"api/users/{user.Id}", Ct));

        await using var context = NewContext();
        Assert.True(await context.Users.AnyAsync(x => x.Id == user.Id, Ct));
    }

    private async Task<UserEntity> SeedUser()
    {
        var user = TestData.User(name: $"User {Guid.NewGuid():N}");
        await Seed(user);

        return user;
    }

    private async Task<UserEntity> StoredUser(Guid id)
    {
        await using var context = NewContext();

        return await context.Users.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }
}
