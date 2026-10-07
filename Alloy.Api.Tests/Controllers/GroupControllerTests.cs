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
using Alloy.Api.Hubs;
using Alloy.Api.Tests.Support;
using Alloy.Api.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Alloy.Api.Tests.Controllers;

/// <summary>
/// <c>GroupController</c> over HTTP. Groups themselves are managed with ManageGroups; their memberships
/// also by a group's own managers (a membership with the Manager role holds ManageMembership on it).
/// </summary>
public class GroupControllerTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    // GET api/groups

    [Fact]
    public async Task GetAll_returns_every_group_to_a_caller_holding_ViewGroups()
    {
        var group = await SeedGroup();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewGroups).SeedAsync();

        var groups = await ReadAsync<List<Group>>(await Client(actor).GetAsync("api/groups", Ct));

        Assert.Contains(group.Id, groups.Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_returns_only_the_managed_groups_to_a_group_manager()
    {
        var other = await SeedGroup();
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Manager).SeedAsync();

        var groups = await ReadAsync<List<Group>>(await Client(actor).GetAsync("api/groups", Ct));

        Assert.Equal(actor.NewGroups, groups.Select(x => x.Id));
        Assert.DoesNotContain(other.Id, groups.Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_plain_member_of_a_group()
    {
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Member).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/groups", Ct));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_holding_only_ViewUsers()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/groups", Ct));
    }

    // GET api/groups/{id}

    [Fact]
    public async Task Get_returns_the_group_to_its_manager()
    {
        var group = await SeedGroup();
        var actor = await Actor().InGroup(group.Id, GroupMembershipRole.Manager).SeedAsync();

        var read = await ReadAsync<Group>(await Client(actor).GetAsync($"api/groups/{group.Id}", Ct));

        Assert.Equal(group.Name, read.Name);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_plain_member_of_the_group()
    {
        var group = await SeedGroup();
        var actor = await Actor().InGroup(group.Id).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/groups/{group.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_manager_of_another_group()
    {
        var group = await SeedGroup();
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Manager).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/groups/{group.Id}", Ct));
    }

    /// <summary>A group that does not exist is answered with a 204 and no body.</summary>
    [Fact]
    public async Task Get_answers_an_unknown_group_with_no_content()
    {
        await AssertStatus(HttpStatusCode.NoContent, await RootClient.GetAsync($"api/groups/{Guid.NewGuid()}", Ct));
    }

    // POST api/groups

    [Fact]
    public async Task Create_by_a_caller_holding_ManageGroups_stores_the_group()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageGroups).SeedAsync();

        var created = await ReadAsync<Group>(await Client(actor).PostAsJsonAsync("api/groups", new Group { Name = "Blue Team" }, AlloyJson, Ct));

        await using var context = NewContext();
        Assert.Equal("Blue Team", (await context.Groups.SingleAsync(x => x.Id == created.Id, Ct)).Name);
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_ViewGroups()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewGroups).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync("api/groups", new Group { Name = "Denied" }, AlloyJson, Ct));

        await using var context = NewContext();
        Assert.False(await context.Groups.AnyAsync(x => x.Name == "Denied", Ct));
    }

    // PUT api/groups/{id}

    [Fact]
    public async Task Update_by_a_caller_holding_ManageGroups_renames_the_group()
    {
        var group = await SeedGroup();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageGroups).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).PutAsJsonAsync($"api/groups/{group.Id}", new Group { Id = group.Id, Name = "Renamed" }, AlloyJson, Ct));

        Assert.Equal("Renamed", (await StoredGroup(group.Id)).Name);
    }

    /// <summary>The body's id is mapped onto the stored row, so an id other than the route's is answered with a 500.</summary>
    [Fact]
    public async Task Update_answers_a_body_id_other_than_the_routes_with_a_server_error()
    {
        var group = await SeedGroup();

        var response = await RootClient.PutAsJsonAsync($"api/groups/{group.Id}", new Group { Id = Guid.NewGuid(), Name = "Renamed" }, AlloyJson, Ct);

        var problem = await AssertJsonError(HttpStatusCode.InternalServerError, response);
        Assert.StartsWith("The property 'GroupEntity.Id' is part of a key", problem.Detail);
        Assert.Equal(group.Name, (await StoredGroup(group.Id)).Name);
    }

    [Fact]
    public async Task Update_is_forbidden_for_the_groups_own_manager()
    {
        var group = await SeedGroup();
        var actor = await Actor().InGroup(group.Id, GroupMembershipRole.Manager).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden,
            await Client(actor).PutAsJsonAsync($"api/groups/{group.Id}", new Group { Id = group.Id, Name = "Renamed" }, AlloyJson, Ct));

        Assert.Equal(group.Name, (await StoredGroup(group.Id)).Name);
    }

    // DELETE api/groups/{id}

    [Fact]
    public async Task Delete_by_a_caller_holding_ManageGroups_removes_the_group_and_its_memberships()
    {
        var group = await SeedGroup();
        var user = TestData.User();
        await Seed(user, TestData.GroupMembership(group.Id, user.Id));
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageGroups).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/groups/{group.Id}", Ct));

        await using var context = NewContext();
        Assert.False(await context.Groups.AnyAsync(x => x.Id == group.Id, Ct));
        Assert.False(await context.GroupMemberships.AnyAsync(x => x.GroupId == group.Id, Ct));
    }

    /// <summary>A group holding an event membership cannot be deleted; the foreign key refusal is answered with a 500.</summary>
    [Fact]
    public async Task Delete_answers_a_group_with_an_event_membership_with_a_server_error()
    {
        var group = await SeedGroup();
        var evt = TestData.Event(null);
        await Seed(evt, TestData.EventMembership(evt.Id, TestData.EventRoles.Member, groupId: group.Id));

        var problem = await AssertJsonError(HttpStatusCode.InternalServerError, await RootClient.DeleteAsync($"api/groups/{group.Id}", Ct));
        Assert.Equal("An error occurred while saving the entity changes. See the inner exception for details.", problem.Detail);

        await using var context = NewContext();
        Assert.True(await context.Groups.AnyAsync(x => x.Id == group.Id, Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_the_groups_own_manager()
    {
        var group = await SeedGroup();
        var actor = await Actor().InGroup(group.Id, GroupMembershipRole.Manager).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).DeleteAsync($"api/groups/{group.Id}", Ct));

        await using var context = NewContext();
        Assert.True(await context.Groups.AnyAsync(x => x.Id == group.Id, Ct));
    }

    // GET api/groups/memberships/{id}, api/groups/{groupId}/memberships

    [Fact]
    public async Task GetMembership_returns_the_membership_to_the_groups_manager()
    {
        var (group, membership) = await SeedMembership();
        var actor = await Actor().InGroup(group.Id, GroupMembershipRole.Manager).SeedAsync();

        var read = await ReadAsync<GroupMembership>(await Client(actor).GetAsync($"api/groups/memberships/{membership.Id}", Ct));

        Assert.Equal((group.Id, membership.UserId), (read.GroupId, read.UserId));
    }

    [Fact]
    public async Task GetMembership_is_forbidden_for_a_manager_of_another_group()
    {
        var (_, membership) = await SeedMembership();
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Manager).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/groups/memberships/{membership.Id}", Ct));
    }

    [Fact]
    public async Task GetMemberships_returns_the_groups_memberships_to_a_caller_holding_ViewGroups()
    {
        var (group, membership) = await SeedMembership();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewGroups).SeedAsync();

        var memberships = await ReadAsync<List<GroupMembership>>(await Client(actor).GetAsync($"api/groups/{group.Id}/memberships", Ct));

        Assert.Equal([membership.Id], memberships.Select(x => x.Id));
    }

    [Fact]
    public async Task GetMemberships_returns_the_groups_memberships_to_its_manager()
    {
        var (group, membership) = await SeedMembership();
        var actor = await Actor().InGroup(group.Id, GroupMembershipRole.Manager).SeedAsync();

        var memberships = await ReadAsync<List<GroupMembership>>(await Client(actor).GetAsync($"api/groups/{group.Id}/memberships", Ct));

        Assert.Contains(membership.Id, memberships.Select(x => x.Id));
    }

    [Fact]
    public async Task GetMemberships_is_forbidden_for_a_plain_member_of_the_group()
    {
        var (group, _) = await SeedMembership();
        var actor = await Actor().InGroup(group.Id).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/groups/{group.Id}/memberships", Ct));
    }

    // POST api/groups/{groupId}/memberships

    [Fact]
    public async Task CreateMembership_by_the_groups_manager_stores_it_under_the_route_group_and_broadcasts_it()
    {
        var group = await SeedGroup();
        var other = await SeedGroup();
        var user = TestData.User();
        await Seed(user);
        var actor = await Actor().InGroup(group.Id, GroupMembershipRole.Manager).SeedAsync();

        var created = await ReadAsync<GroupMembership>(await Client(actor).PostAsJsonAsync($"api/groups/{group.Id}/memberships",
            new GroupMembership { GroupId = other.Id, UserId = user.Id }, AlloyJson, Ct));

        await using var context = NewContext();
        Assert.Equal(group.Id, (await context.GroupMemberships.SingleAsync(x => x.Id == created.Id, Ct)).GroupId);
        var broadcast = Assert.Single(Factory.Hub.ToGroup(group.Id));
        Assert.Equal(EngineHubMethods.GroupMembershipCreated, broadcast.Method);
        Assert.Equal(created.Id, Assert.IsType<GroupMembership>(broadcast.Arguments[0]).Id);
    }

    /// <summary>The second membership of one user is refused by the unique index on (group, user), answered with a 500.</summary>
    [Fact]
    public async Task CreateMembership_answers_a_user_already_in_the_group_with_a_server_error()
    {
        var (group, membership) = await SeedMembership();

        var response = await RootClient.PostAsJsonAsync($"api/groups/{group.Id}/memberships",
            new GroupMembership { GroupId = group.Id, UserId = membership.UserId }, AlloyJson, Ct);

        var problem = await AssertJsonError(HttpStatusCode.InternalServerError, response);
        Assert.Equal("An error occurred while saving the entity changes. See the inner exception for details.", problem.Detail);
    }

    [Fact]
    public async Task CreateMembership_is_forbidden_for_a_plain_member_of_the_group()
    {
        var group = await SeedGroup();
        var actor = await Actor().InGroup(group.Id).SeedAsync();
        var user = TestData.User();
        await Seed(user);

        var response = await Client(actor).PostAsJsonAsync($"api/groups/{group.Id}/memberships",
            new GroupMembership { GroupId = group.Id, UserId = user.Id }, AlloyJson, Ct);

        await AssertJsonError(HttpStatusCode.Forbidden, response);
        await using var context = NewContext();
        Assert.False(await context.GroupMemberships.AnyAsync(x => x.UserId == user.Id, Ct));
    }

    [Fact]
    public async Task CreateMembership_is_forbidden_for_a_manager_of_another_group()
    {
        var group = await SeedGroup();
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/groups/{group.Id}/memberships",
            new GroupMembership { GroupId = group.Id, UserId = actor.Id, Role = GroupMembershipRole.Manager }, AlloyJson, Ct);

        await AssertJsonError(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task CreateMembership_is_forbidden_for_a_caller_holding_only_ViewGroups()
    {
        var group = await SeedGroup();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewGroups).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/groups/{group.Id}/memberships",
            new GroupMembership { GroupId = group.Id, UserId = actor.Id }, AlloyJson, Ct);

        await AssertJsonError(HttpStatusCode.Forbidden, response);
    }

    // PUT api/groups/memberships/{id}

    [Fact]
    public async Task UpdateMembership_by_the_groups_manager_changes_the_role()
    {
        var (group, membership) = await SeedMembership();
        var actor = await Actor().InGroup(group.Id, GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/groups/memberships/{membership.Id}",
            new GroupMembership { Id = membership.Id, GroupId = group.Id, UserId = membership.UserId, Role = GroupMembershipRole.Manager }, AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(GroupMembershipRole.Manager, (await StoredMembership(membership.Id)).Role);
    }

    [Fact]
    public async Task UpdateMembership_is_forbidden_for_a_manager_of_another_group()
    {
        var (group, membership) = await SeedMembership();
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Manager).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/groups/memberships/{membership.Id}",
            new GroupMembership { Id = membership.Id, GroupId = group.Id, UserId = membership.UserId, Role = GroupMembershipRole.Manager }, AlloyJson, Ct);

        await AssertJsonError(HttpStatusCode.Forbidden, response);
        Assert.Equal(GroupMembershipRole.Member, (await StoredMembership(membership.Id)).Role);
    }

    // DELETE api/groups/memberships/{id}

    [Fact]
    public async Task DeleteMembership_by_a_caller_holding_ManageGroups_removes_it_and_broadcasts_it()
    {
        var (group, membership) = await SeedMembership();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageGroups).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/groups/memberships/{membership.Id}", Ct));

        await using var context = NewContext();
        Assert.False(await context.GroupMemberships.AnyAsync(x => x.Id == membership.Id, Ct));
        var broadcast = Assert.Single(Factory.Hub.ToGroup(group.Id));
        Assert.Equal((EngineHubMethods.GroupMembershipDeleted, (object)membership.Id), (broadcast.Method, broadcast.Argument));
    }

    [Fact]
    public async Task DeleteMembership_by_the_groups_manager_removes_it()
    {
        var (group, membership) = await SeedMembership();
        var actor = await Actor().InGroup(group.Id, GroupMembershipRole.Manager).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/groups/memberships/{membership.Id}", Ct));

        await using var context = NewContext();
        Assert.False(await context.GroupMemberships.AnyAsync(x => x.Id == membership.Id, Ct));
    }

    [Fact]
    public async Task DeleteMembership_is_forbidden_for_a_plain_member_of_the_group()
    {
        var (group, membership) = await SeedMembership();
        var actor = await Actor().InGroup(group.Id).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).DeleteAsync($"api/groups/memberships/{membership.Id}", Ct));

        await using var context = NewContext();
        Assert.True(await context.GroupMemberships.AnyAsync(x => x.Id == membership.Id, Ct));
    }

    private async Task<GroupEntity> SeedGroup()
    {
        var group = TestData.Group($"Group {Guid.NewGuid():N}");
        await Seed(group);

        return group;
    }

    private async Task<(GroupEntity Group, GroupMembershipEntity Membership)> SeedMembership()
    {
        var group = await SeedGroup();
        var user = TestData.User();
        var membership = TestData.GroupMembership(group.Id, user.Id);
        await Seed(user, membership);

        return (group, membership);
    }

    private async Task<GroupEntity> StoredGroup(Guid id)
    {
        await using var context = NewContext();

        return await context.Groups.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }

    private async Task<GroupMembershipEntity> StoredMembership(Guid id)
    {
        await using var context = NewContext();

        return await context.GroupMemberships.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }
}
