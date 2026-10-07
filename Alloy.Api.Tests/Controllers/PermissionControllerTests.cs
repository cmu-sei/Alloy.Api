// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Authorization;
using Alloy.Api.Tests.Support;
using Alloy.Api.ViewModels;

namespace Alloy.Api.Tests.Controllers;

/// <summary>
/// The "my permissions" routes, the role lists, and the routes that relay a sibling API: none has a gate
/// of its own beyond being signed in (the role lists need ViewRoles), and each answers from the caller's
/// claims as the real transformer derived them.
/// </summary>
public class PermissionControllerTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    [Fact]
    public async Task GetMySystemPermissions_returns_the_callers_system_permissions()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEvents, SystemPermission.ManageGroups).SeedAsync();

        var permissions = await ReadAsync<List<SystemPermission>>(await Client(actor).GetAsync("api/me/systemPermissions", Ct));

        Assert.Equal([SystemPermission.ViewEvents, SystemPermission.ManageGroups], permissions.Order());
    }

    /// <summary>The event in the route is not used: the caller's permissions on every event come back.</summary>
    [Fact]
    public async Task GetMyEventPermissions_returns_the_callers_permissions_on_every_event_whatever_the_route_names()
    {
        var actor = await Actor()
            .OnNewEvent(null, EventPermission.ViewEvent)
            .OnNewEvent(null, EventPermission.ManageEvent)
            .SeedAsync();

        var claims = await ReadAsync<List<EventPermissionClaim>>(await Client(actor).GetAsync($"api/events/{actor.NewEvents[0]}/me/permissions", Ct));

        Assert.Equal(actor.NewEvents.Order(), claims.Select(x => x.EventId).Order());
    }

    /// <summary>The template in the route is not used: the caller's permissions on every template come back.</summary>
    [Fact]
    public async Task GetMyEventTemplatePermissions_returns_the_callers_permissions_on_every_template_whatever_the_route_names()
    {
        var actor = await Actor()
            .OnNewEventTemplate(EventTemplatePermission.ViewEventTemplate)
            .OnNewEventTemplate(EventTemplatePermission.EditEventTemplate)
            .SeedAsync();

        var claims = await ReadAsync<List<EventTemplatePermissionClaim>>(
            await Client(actor).GetAsync($"api/eventTemplates/{actor.NewEventTemplates[0]}/me/permissions", Ct));

        Assert.Equal(actor.NewEventTemplates.Order(), claims.Select(x => x.EventTemplateId).Order());
    }

    [Fact]
    public async Task GetMyGroupPermissions_for_one_group_returns_only_that_groups_claim()
    {
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Manager).OnNewGroup(GroupMembershipRole.Manager).SeedAsync();

        var claims = await ReadAsync<List<GroupPermissionsClaim>>(await Client(actor).GetAsync($"api/permissions/group/mine?groupId={actor.NewGroups[1]}", Ct));

        var claim = Assert.Single(claims);
        Assert.Equal(actor.NewGroups[1], claim.GroupId);
        Assert.Equal([GroupPermission.ManageMembership], claim.Permissions);
    }

    [Fact]
    public async Task GetEventRoles_returns_the_seeded_roles_to_a_caller_holding_ViewRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        var roles = await ReadAsync<List<EventRole>>(await Client(actor).GetAsync("api/event-roles", Ct));

        Assert.Equal(
            new[] { TestData.EventRoles.Manager, TestData.EventRoles.Observer, TestData.EventRoles.Member }.Order(),
            roles.Select(x => x.Id).Order());
    }

    [Fact]
    public async Task GetEventRoles_is_forbidden_for_a_caller_holding_only_ViewUsers()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers, SystemPermission.ViewEvents).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/event-roles", Ct));
    }

    [Fact]
    public async Task GetEventRole_returns_the_role_to_a_caller_holding_ViewRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        var role = await ReadAsync<EventRole>(await Client(actor).GetAsync($"api/event-roles/{TestData.EventRoles.Observer}", Ct));

        Assert.Equal("Observer", role.Name);
    }

    [Fact]
    public async Task GetEventRole_is_forbidden_for_a_caller_holding_only_ManageEvents()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEvents).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/event-roles/{TestData.EventRoles.Observer}", Ct));
    }

    [Fact]
    public async Task GetEventTemplateRoles_returns_the_seeded_roles_to_a_caller_holding_ViewRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        var roles = await ReadAsync<List<EventTemplateRole>>(await Client(actor).GetAsync("api/eventTemplate-roles", Ct));

        Assert.Contains(TestData.EventTemplateRoles.Manager, roles.Select(x => x.Id));
    }

    [Fact]
    public async Task GetEventTemplateRoles_is_forbidden_for_a_caller_holding_only_ViewEventTemplates()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEventTemplates).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/eventTemplate-roles", Ct));
    }

    [Fact]
    public async Task GetEventTemplateRole_returns_the_role_to_a_caller_holding_ViewRoles()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewRoles).SeedAsync();

        var role = await ReadAsync<EventTemplateRole>(await Client(actor).GetAsync($"api/eventTemplate-roles/{TestData.EventTemplateRoles.Observer}", Ct));

        Assert.Equal("Observer", role.Name);
    }

    [Fact]
    public async Task GetEventTemplateRole_of_an_unknown_role_is_not_found()
    {
        await AssertJsonError(HttpStatusCode.NotFound, await RootClient.GetAsync($"api/eventTemplate-roles/{Guid.NewGuid()}", Ct));
    }

    [Fact]
    public async Task GetEventTemplateRole_is_forbidden_for_a_caller_holding_only_ManageEventTemplates()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEventTemplates).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/eventTemplate-roles/{TestData.EventTemplateRoles.Member}", Ct));
    }
}
