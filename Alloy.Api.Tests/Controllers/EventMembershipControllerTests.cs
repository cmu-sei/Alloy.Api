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
/// <c>EventMembershipController</c> over HTTP: reading needs ViewEvent and writing ManageEvent on the
/// membership's event (or the matching system permission).
/// </summary>
public class EventMembershipControllerTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    // GET api/events/memberships/{id}

    [Fact]
    public async Task Get_returns_the_membership_to_a_member_holding_ViewEvent_on_its_event()
    {
        var (evt, membership) = await SeedMembership();
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ViewEvent]).SeedAsync();

        var read = await ReadAsync<EventMembership>(await Client(actor).GetAsync($"api/events/memberships/{membership.Id}", Ct));

        Assert.Equal((membership.EventId, membership.UserId, membership.RoleId), (read.EventId, read.UserId, read.RoleId));
    }

    [Fact]
    public async Task Get_returns_the_membership_to_a_caller_holding_ViewEvents()
    {
        var (_, membership) = await SeedMembership();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEvents).SeedAsync();

        var read = await ReadAsync<EventMembership>(await Client(actor).GetAsync($"api/events/memberships/{membership.Id}", Ct));

        Assert.Equal(membership.Id, read.Id);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_ViewEvent_only_on_another_event()
    {
        var (evt, membership) = await SeedMembership();
        var actor = await Actor().OnNewEvent(evt.EventTemplateId, EventPermission.ViewEvent).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/events/memberships/{membership.Id}", Ct));
    }

    [Fact]
    public async Task Get_of_an_unknown_membership_is_not_found()
    {
        await AssertJsonError(HttpStatusCode.NotFound, await RootClient.GetAsync($"api/events/memberships/{Guid.NewGuid()}", Ct));
    }

    // GET api/events/{id}/memberships

    [Fact]
    public async Task GetAll_returns_the_events_memberships_to_a_caller_holding_ViewEvents()
    {
        var (evt, membership) = await SeedMembership();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEvents).SeedAsync();

        var memberships = await ReadAsync<List<EventMembership>>(await Client(actor).GetAsync($"api/events/{evt.Id}/memberships", Ct));

        Assert.Equal([membership.Id], memberships.Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_returns_the_events_memberships_to_a_member_holding_ViewEvent()
    {
        var (evt, membership) = await SeedMembership();
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ViewEvent]).SeedAsync();

        var memberships = await ReadAsync<List<EventMembership>>(await Client(actor).GetAsync($"api/events/{evt.Id}/memberships", Ct));

        Assert.Contains(membership.Id, memberships.Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_member_holding_only_EditEvent()
    {
        var (evt, _) = await SeedMembership();
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.EditEvent]).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/events/{evt.Id}/memberships", Ct));
    }

    // POST api/events/{eventId}/memberships

    [Fact]
    public async Task Create_by_a_member_holding_ManageEvent_stores_the_membership()
    {
        var evt = await SeedEvent();
        var user = TestData.User();
        await Seed(user);
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ManageEvent]).SeedAsync();

        var created = await ReadAsync<EventMembership>(await Client(actor).PostAsJsonAsync($"api/events/{evt.Id}/memberships",
            new EventMembership { EventId = evt.Id, UserId = user.Id, RoleId = TestData.EventRoles.Observer }, AlloyJson, Ct));

        await using var context = NewContext();
        var saved = await context.EventMemberships.SingleAsync(x => x.Id == created.Id, Ct);
        Assert.Equal((evt.Id, user.Id, TestData.EventRoles.Observer), (saved.EventId, saved.UserId, saved.RoleId));
    }

    [Fact]
    public async Task Create_by_a_caller_holding_ManageEvents_stores_the_membership()
    {
        var evt = await SeedEvent();
        var user = TestData.User();
        await Seed(user);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEvents).SeedAsync();

        var created = await ReadAsync<EventMembership>(await Client(actor).PostAsJsonAsync($"api/events/{evt.Id}/memberships",
            new EventMembership { EventId = evt.Id, UserId = user.Id, RoleId = TestData.EventRoles.Member }, AlloyJson, Ct));

        Assert.Equal(user.Id, (await StoredMembership(created.Id)).UserId);
    }

    /// <summary>A caller holding only ManageEvent adds a membership with the all-permissions Manager role.</summary>
    [Fact]
    public async Task Create_stores_a_Manager_membership_added_by_a_caller_holding_only_ManageEvent()
    {
        var evt = await SeedEvent();
        var user = TestData.User();
        await Seed(user);
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ManageEvent]).SeedAsync();

        var created = await ReadAsync<EventMembership>(await Client(actor).PostAsJsonAsync($"api/events/{evt.Id}/memberships",
            new EventMembership { EventId = evt.Id, UserId = user.Id, RoleId = TestData.EventRoles.Manager }, AlloyJson, Ct));

        Assert.Equal(TestData.EventRoles.Manager, (await StoredMembership(created.Id)).RoleId);
    }

    /// <summary>A new membership is broadcast to the group named for the membership's own id.</summary>
    [Fact]
    public async Task Create_broadcasts_the_membership_to_the_group_of_its_own_id_and_not_its_events()
    {
        var evt = await SeedEvent();
        var user = TestData.User();
        await Seed(user);

        var created = await ReadAsync<EventMembership>(await RootClient.PostAsJsonAsync($"api/events/{evt.Id}/memberships",
            new EventMembership { EventId = evt.Id, UserId = user.Id, RoleId = TestData.EventRoles.Member }, AlloyJson, Ct));

        Assert.Equal(EngineHubMethods.EventMembershipCreated, Assert.Single(Factory.Hub.ToGroup(created.Id)).Method);
        Assert.Empty(Factory.Hub.ToGroup(evt.Id));
    }

    /// <summary>The unique index on (event, user, group) does not hold while the group is null, so a user can be added twice.</summary>
    [Fact]
    public async Task Create_stores_a_second_membership_of_the_same_user_on_the_same_event()
    {
        var evt = await SeedEvent();
        var user = TestData.User();
        await Seed(user, TestData.EventMembership(evt.Id, TestData.EventRoles.Member, user.Id));

        var response = await RootClient.PostAsJsonAsync($"api/events/{evt.Id}/memberships",
            new EventMembership { EventId = evt.Id, UserId = user.Id, RoleId = TestData.EventRoles.Member }, AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        await using var context = NewContext();
        Assert.Equal(2, await context.EventMemberships.CountAsync(x => x.EventId == evt.Id && x.UserId == user.Id, Ct));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_member_holding_only_EditEvent()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ViewEvent, EventPermission.EditEvent]).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/events/{evt.Id}/memberships",
            new EventMembership { EventId = evt.Id, UserId = actor.Id, RoleId = TestData.EventRoles.Manager }, AlloyJson, Ct);

        await AssertJsonError(HttpStatusCode.Forbidden, response);
        await using var context = NewContext();
        Assert.False(await context.EventMemberships.AnyAsync(x => x.EventId == evt.Id && x.RoleId == TestData.EventRoles.Manager, Ct));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_ManageEvent_only_on_another_event()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnNewEvent(evt.EventTemplateId, EventPermission.ManageEvent).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/events/{evt.Id}/memberships",
            new EventMembership { EventId = evt.Id, UserId = actor.Id, RoleId = TestData.EventRoles.Manager }, AlloyJson, Ct);

        await AssertJsonError(HttpStatusCode.Forbidden, response);
    }

    // PUT api/Events/Memberships/{id}

    [Fact]
    public async Task Update_by_a_member_holding_ManageEvent_changes_the_role()
    {
        var (evt, membership) = await SeedMembership();
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ManageEvent]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/Events/Memberships/{membership.Id}",
            new EventMembership { Id = membership.Id, EventId = evt.Id, RoleId = TestData.EventRoles.Manager }, AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(TestData.EventRoles.Manager, (await StoredMembership(membership.Id)).RoleId);
    }

    [Fact]
    public async Task Update_by_a_caller_holding_ManageEvents_changes_the_role()
    {
        var (evt, membership) = await SeedMembership();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEvents).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/Events/Memberships/{membership.Id}",
            new EventMembership { Id = membership.Id, EventId = evt.Id, RoleId = TestData.EventRoles.Observer }, AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(TestData.EventRoles.Observer, (await StoredMembership(membership.Id)).RoleId);
    }

    /// <summary>A caller managing the event named in the body changes the role of a membership on another event.</summary>
    [Fact]
    public async Task Update_changes_a_membership_of_another_event_for_a_caller_managing_the_event_named_in_the_body()
    {
        var (_, membership) = await SeedMembership();
        var actor = await Actor().OnNewEvent(null, EventPermission.ManageEvent).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/Events/Memberships/{membership.Id}",
            new EventMembership { Id = membership.Id, EventId = Assert.Single(actor.NewEvents), RoleId = TestData.EventRoles.Manager }, AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(TestData.EventRoles.Manager, (await StoredMembership(membership.Id)).RoleId);
    }

    [Fact]
    public async Task Update_is_forbidden_for_a_member_holding_only_ViewEvent()
    {
        var (evt, membership) = await SeedMembership();
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ViewEvent]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/Events/Memberships/{membership.Id}",
            new EventMembership { Id = membership.Id, EventId = evt.Id, RoleId = TestData.EventRoles.Manager }, AlloyJson, Ct);

        await AssertJsonError(HttpStatusCode.Forbidden, response);
        Assert.Equal(TestData.EventRoles.Member, (await StoredMembership(membership.Id)).RoleId);
    }

    // DELETE api/events/memberships/{id}

    // Same case as Create_broadcasts_the_membership_to_the_group_of_its_own_id_and_not_its_events.
    [Fact]
    public async Task Delete_by_a_caller_holding_ManageEvents_removes_the_membership_and_broadcasts_it()
    {
        var (_, membership) = await SeedMembership();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEvents).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/events/memberships/{membership.Id}", Ct));

        await using var context = NewContext();
        Assert.False(await context.EventMemberships.AnyAsync(x => x.Id == membership.Id, Ct));
        Assert.Equal(EngineHubMethods.EventMembershipDeleted, Assert.Single(Factory.Hub.ToGroup(membership.Id)).Method);
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_ManageEvent_only_on_another_event()
    {
        var (evt, membership) = await SeedMembership();
        var actor = await Actor().OnNewEvent(evt.EventTemplateId, EventPermission.ManageEvent).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).DeleteAsync($"api/events/memberships/{membership.Id}", Ct));

        await using var context = NewContext();
        Assert.True(await context.EventMemberships.AnyAsync(x => x.Id == membership.Id, Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_EditEvents()
    {
        var (_, membership) = await SeedMembership();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditEvents).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).DeleteAsync($"api/events/memberships/{membership.Id}", Ct));
    }

    private async Task<EventEntity> SeedEvent()
    {
        var template = TestData.EventTemplate();
        var evt = TestData.Event(template.Id);
        await Seed(template, evt);

        return evt;
    }

    private async Task<(EventEntity Event, EventMembershipEntity Membership)> SeedMembership()
    {
        var evt = await SeedEvent();
        var user = TestData.User();
        var membership = TestData.EventMembership(evt.Id, TestData.EventRoles.Member, user.Id);
        await Seed(user, membership);

        return (evt, membership);
    }

    private async Task<EventMembershipEntity> StoredMembership(Guid id)
    {
        await using var context = NewContext();

        return await context.EventMemberships.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }
}
