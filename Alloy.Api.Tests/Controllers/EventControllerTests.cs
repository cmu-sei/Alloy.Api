// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
/// <c>EventController</c> over HTTP: each route's gate (a system permission, or the event permission on
/// the event the route names), what a write stores, and what it broadcasts.
/// </summary>
public class EventControllerTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    /// <summary>The message of a parameterless <c>ForbiddenException</c>, which every permission check throws.</summary>
    private const string InsufficientPermissions = "Insufficient Permissions";

    // GET api/events

    [Fact]
    public async Task GetAll_returns_every_event_to_a_caller_holding_ViewEvents()
    {
        var evt = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEvents).SeedAsync();

        var events = await ReadAsync<List<Event>>(await Client(actor).GetAsync("api/events", Ct));

        Assert.Contains(evt.Id, events.Select(x => x.Id));
    }

    /// <summary>Without ViewEvents the list falls back to the caller's own memberships.</summary>
    [Fact]
    public async Task GetAll_returns_only_the_callers_memberships_to_a_caller_holding_only_ViewEventTemplates()
    {
        var other = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEventTemplates)
            .OnNewEvent(null, EventPermission.ViewEvent).SeedAsync();

        var events = await ReadAsync<List<Event>>(await Client(actor).GetAsync("api/events", Ct));

        Assert.Equal([Assert.Single(actor.NewEvents)], events.Select(x => x.Id));
        Assert.DoesNotContain(other.Id, events.Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_adds_the_callers_event_and_system_permissions_to_each_event()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEventTemplates)
            .OnNewEvent(null, EventPermission.ViewEvent).SeedAsync();

        var events = await ReadAsync<List<Event>>(await Client(actor).GetAsync("api/events", Ct));

        var permissions = Assert.Single(events).EventPermissions.ToList();
        Assert.Contains(nameof(SystemPermission.ViewEventTemplates), permissions);
        Assert.Contains(permissions, x => x.Contains(Assert.Single(actor.NewEvents).ToString()));
    }

    // GET api/events/{id}

    [Fact]
    public async Task Get_returns_the_event_to_a_caller_holding_ViewEvents()
    {
        var evt = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEvents).SeedAsync();

        var read = await ReadAsync<Event>(await Client(actor).GetAsync($"api/events/{evt.Id}", Ct));

        Assert.Equal(evt.Name, read.Name);
    }

    [Fact]
    public async Task Get_returns_the_event_to_a_member_holding_ViewEvent_on_it()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ViewEvent]).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/events/{evt.Id}", Ct));
    }

    [Fact]
    public async Task Get_returns_the_event_to_a_member_of_a_group_holding_ViewEvent_on_it()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ViewEvent], throughGroup: true).SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/events/{evt.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_ViewEventTemplates()
    {
        var evt = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEventTemplates).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/events/{evt.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_member_holding_only_EditEvent()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.EditEvent]).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/events/{evt.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_ViewEvent_only_on_another_event()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnNewEvent(evt.EventTemplateId, EventPermission.ViewEvent).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/events/{evt.Id}", Ct));
    }

    [Fact]
    public async Task Get_answers_an_unknown_event_with_a_not_found_naming_the_event()
    {
        var id = Guid.NewGuid();

        var problem = await AssertJsonError(HttpStatusCode.NotFound, await RootClient.GetAsync($"api/events/{id}", Ct));

        Assert.Equal($"Event {id} was not found.", problem.Title);
    }

    // GET api/events/{id}/error-detail

    [Fact]
    public async Task GetErrorDetail_returns_the_stored_detail_to_a_caller_holding_ManageEvents()
    {
        var evt = await SeedEvent(x =>
        {
            x.ErrorMessage = "Launch failed.";
            x.ErrorDetail = "terraform: host 10.0.0.1 unreachable";
        });
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEvents).SeedAsync();

        var detail = await ReadAsync<EventErrorDetail>(await Client(actor).GetAsync($"api/events/{evt.Id}/error-detail", Ct));

        Assert.Equal((evt.Id, "Launch failed.", "terraform: host 10.0.0.1 unreachable"), (detail.EventId, detail.ErrorMessage, detail.ErrorDetail));
    }

    /// <summary>The event-scoped ManageEvent a launch gives its own user is deliberately not enough.</summary>
    [Fact]
    public async Task GetErrorDetail_is_forbidden_for_a_caller_holding_only_ManageEvent_on_the_event()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnEvent(evt.Id, roleId: TestData.EventRoles.Manager).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/events/{evt.Id}/error-detail", Ct));
    }

    [Fact]
    public async Task GetErrorDetail_is_forbidden_for_a_caller_holding_only_EditEvents()
    {
        var evt = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditEvents).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/events/{evt.Id}/error-detail", Ct));
    }

    // PUT api/events/{id}

    [Fact]
    public async Task Update_by_a_member_holding_EditEvent_stores_the_editable_fields()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.EditEvent]).SeedAsync();
        var expiration = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        var response = await Client(actor).PutAsJsonAsync($"api/events/{evt.Id}",
            new UpdateEventRequest { Name = "Renamed", Description = "Changed", ExpirationDate = expiration }, AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var saved = await Stored(evt.Id);
        Assert.Equal(("Renamed", "Changed", expiration, actor.Id), (saved.Name, saved.Description, saved.ExpirationDate, saved.ModifiedBy));
    }

    [Fact]
    public async Task Update_by_a_caller_holding_EditEvents_stores_the_change()
    {
        var evt = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditEvents).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/events/{evt.Id}", new UpdateEventRequest { Name = "Renamed" }, AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("Renamed", (await Stored(evt.Id)).Name);
    }

    [Fact]
    public async Task Update_broadcasts_the_modified_properties()
    {
        var evt = await SeedEvent();

        await RootClient.PutAsJsonAsync($"api/events/{evt.Id}", new UpdateEventRequest { Name = "Renamed", Description = evt.Description }, AlloyJson, Ct);

        var broadcast = Assert.Single(Factory.Hub.ToGroup(evt.Id));
        Assert.Equal(EngineHubMethods.EventUpdated, broadcast.Method);
        Assert.Contains("name", Assert.IsType<string[]>(broadcast.Arguments[1]));
    }

    [Fact]
    public async Task Update_is_forbidden_for_a_member_holding_only_ViewEvent()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ViewEvent]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/events/{evt.Id}", new UpdateEventRequest { Name = "Renamed" }, AlloyJson, Ct);

        await AssertJsonError(HttpStatusCode.Forbidden, response);
        Assert.Equal(evt.Name, (await Stored(evt.Id)).Name);
    }

    [Fact]
    public async Task Update_is_forbidden_for_a_caller_holding_EditEvent_only_on_another_event()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnNewEvent(evt.EventTemplateId, EventPermission.EditEvent).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/events/{evt.Id}", new UpdateEventRequest { Name = "Renamed" }, AlloyJson, Ct);

        await AssertJsonError(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Update_is_forbidden_for_a_caller_holding_only_ViewEvents()
    {
        var evt = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEvents).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/events/{evt.Id}", new UpdateEventRequest { Name = "Renamed" }, AlloyJson, Ct);

        await AssertJsonError(HttpStatusCode.Forbidden, response);
    }

    // DELETE api/events/{id}

    [Fact]
    public async Task Delete_by_a_member_holding_ManageEvent_removes_the_event_and_broadcasts_it()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ManageEvent]).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/events/{evt.Id}", Ct));

        await using var context = NewContext();
        Assert.False(await context.Events.AnyAsync(x => x.Id == evt.Id, Ct));
        Assert.False(await context.EventMemberships.AnyAsync(x => x.EventId == evt.Id, Ct));
        var broadcast = Assert.Single(Factory.Hub.ToGroup(evt.Id), x => x.Method == EngineHubMethods.EventDeleted);
        Assert.Equal(evt.Id, broadcast.Argument);
    }

    [Fact]
    public async Task Delete_by_a_caller_holding_ManageEvents_removes_the_event()
    {
        var evt = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEvents).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/events/{evt.Id}", Ct));

        await using var context = NewContext();
        Assert.False(await context.Events.AnyAsync(x => x.Id == evt.Id, Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_member_holding_only_EditEvent()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.EditEvent, EventPermission.ExecuteEvent]).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).DeleteAsync($"api/events/{evt.Id}", Ct));

        await using var context = NewContext();
        Assert.True(await context.Events.AnyAsync(x => x.Id == evt.Id, Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_EditEvents()
    {
        var evt = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditEvents).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).DeleteAsync($"api/events/{evt.Id}", Ct));
    }

    // DELETE api/events/{id}/end

    [Fact]
    public async Task End_by_a_member_holding_ManageEvent_records_the_end_request()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ManageEvent]).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/events/{evt.Id}/end", Ct));

        var saved = await Stored(evt.Id);
        Assert.NotNull(saved.EndRequestedAt);
        Assert.Equal(EventStatus.Active, saved.Status);
    }

    [Fact]
    public async Task End_by_a_caller_holding_ManageEvents_records_the_end_request()
    {
        var evt = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEvents).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/events/{evt.Id}/end", Ct));

        Assert.NotNull((await Stored(evt.Id)).EndRequestedAt);
    }

    [Theory]
    [InlineData(EventStatus.Ended)]
    [InlineData(EventStatus.Expired)]
    public async Task End_of_a_completed_event_records_no_end_request(EventStatus status)
    {
        var evt = await SeedEvent(x => x.Status = status);

        await AssertStatus(HttpStatusCode.NoContent, await RootClient.DeleteAsync($"api/events/{evt.Id}/end", Ct));

        Assert.Null((await Stored(evt.Id)).EndRequestedAt);
    }

    [Fact]
    public async Task End_is_forbidden_for_a_caller_holding_ManageEvent_only_on_another_event()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnNewEvent(evt.EventTemplateId, EventPermission.ManageEvent).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).DeleteAsync($"api/events/{evt.Id}/end", Ct));

        Assert.Null((await Stored(evt.Id)).EndRequestedAt);
    }

    [Fact]
    public async Task End_is_forbidden_for_a_caller_holding_only_ExecuteEvents()
    {
        var evt = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ExecuteEvents).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).DeleteAsync($"api/events/{evt.Id}/end", Ct));
    }

    // POST api/events/{id}/redeploy

    /// <summary>Caster is asked with Alloy's own resource-owner token, not the caller's.</summary>
    [Fact]
    public async Task Redeploy_by_a_member_holding_ManageEvent_taints_the_workspace_and_schedules_a_redeploy()
    {
        var evt = await SeedEvent(x => x.WorkspaceId = Guid.NewGuid());
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ManageEvent]).SeedAsync();
        var taint = $"http://localhost:4309/api/workspaces/{evt.WorkspaceId}/resources/actions/taint";
        Factory.OutboundHttp.RespondJson(taint, new { resources = new[] { new { id = "vm", tainted = true } } });

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).PostAsync($"api/events/{evt.Id}/redeploy", null, Ct));

        var saved = await Stored(evt.Id);
        Assert.Equal((EventStatus.Planning, InternalEventStatus.PlanningRedeploy), (saved.Status, saved.InternalStatus));
        Assert.Equal($"Bearer {TestIdentity.AccessToken}", Assert.Single(Factory.OutboundHttp.Sent, x => x.Uri == taint).Headers["Authorization"]);
    }

    [Fact]
    public async Task Redeploy_by_a_caller_holding_ManageEvents_schedules_a_redeploy()
    {
        var evt = await SeedEvent(x => x.WorkspaceId = Guid.NewGuid());
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEvents).SeedAsync();
        Factory.OutboundHttp.RespondJson($"http://localhost:4309/api/workspaces/{evt.WorkspaceId}/resources/actions/taint", new { resources = new[] { new { id = "vm", tainted = true } } });

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).PostAsync($"api/events/{evt.Id}/redeploy", null, Ct));

        Assert.Equal(EventStatus.Planning, (await Stored(evt.Id)).Status);
    }

    [Fact]
    public async Task Redeploy_answers_an_event_that_is_not_active_with_a_conflict()
    {
        var evt = await SeedEvent(x => x.Status = EventStatus.Ended);

        var problem = await AssertJsonError(HttpStatusCode.Conflict, await RootClient.PostAsync($"api/events/{evt.Id}/redeploy", null, Ct));

        Assert.Equal("This event is no longer active, so it can't be redeployed.", problem.Title);
    }

    /// <summary>Each status the user can meet gets its own reason; an end leaves EndRequestedAt, and EndDate once ended.</summary>
    [Theory]
    [InlineData(EventStatus.Creating, false, false, "This event is still launching. It can be redeployed once it is active.")]
    [InlineData(EventStatus.Paused, false, false, "This event is paused. Only an active event can be redeployed.")]
    [InlineData(EventStatus.Ending, true, false, "This event is ending, so it can no longer be redeployed.")]
    [InlineData(EventStatus.Ended, true, true, "This event is no longer active, so it can't be redeployed.")]
    [InlineData(EventStatus.Failed, false, false, "This event is no longer active, so it can't be redeployed.")]
    public async Task Redeploy_answers_each_inactive_status_with_its_own_reason(EventStatus status, bool endRequested, bool ended, string message)
    {
        var evt = await SeedEvent(x =>
        {
            x.Status = status;
            x.WorkspaceId = Guid.NewGuid();
            x.EndRequestedAt = endRequested ? TestData.DefaultDate : null;
            x.EndDate = ended ? TestData.DefaultDate : null;
        });

        var problem = await AssertJsonError(HttpStatusCode.Conflict, await RootClient.PostAsync($"api/events/{evt.Id}/redeploy", null, Ct));

        Assert.Equal(message, problem.Title);
        Assert.DoesNotContain(Factory.OutboundHttp.Sent, x => x.Uri.Contains(evt.WorkspaceId.ToString()));
    }

    [Fact]
    public async Task Redeploy_answers_an_event_with_no_workspace_with_a_conflict()
    {
        var evt = await SeedEvent(x => x.WorkspaceId = null);

        var problem = await AssertJsonError(HttpStatusCode.Conflict, await RootClient.PostAsync($"api/events/{evt.Id}/redeploy", null, Ct));

        Assert.Equal("This event has no Caster workspace, so there is nothing to redeploy.", problem.Title);
    }

    [Fact]
    public async Task Redeploy_answers_a_Caster_failure_with_a_bad_gateway_naming_Caster()
    {
        var evt = await SeedEvent(x => x.WorkspaceId = Guid.NewGuid());
        Factory.OutboundHttp.RespondWithStatus($"http://localhost:4309/api/workspaces/{evt.WorkspaceId}/resources/actions/taint", HttpStatusCode.ServiceUnavailable);

        var problem = await AssertJsonError(HttpStatusCode.BadGateway, await RootClient.PostAsync($"api/events/{evt.Id}/redeploy", null, Ct));

        Assert.Equal("Could not mark the event's resources for redeployment. Caster reported an error (HTTP 503).", problem.Title);
        Assert.Equal(EventStatus.Active, (await Stored(evt.Id)).Status);
    }

    [Fact]
    public async Task Redeploy_answers_resources_Caster_did_not_taint_with_a_bad_gateway_counting_them()
    {
        var evt = await SeedEvent(x => x.WorkspaceId = Guid.NewGuid());
        Factory.OutboundHttp.RespondJson($"http://localhost:4309/api/workspaces/{evt.WorkspaceId}/resources/actions/taint", new
        {
            resources = new[] { new { id = "a", tainted = true }, new { id = "b", tainted = false } }
        });

        var problem = await AssertJsonError(HttpStatusCode.BadGateway, await RootClient.PostAsync($"api/events/{evt.Id}/redeploy", null, Ct));

        Assert.StartsWith("Caster did not mark 1 of the event's 2 resources for redeployment", problem.Title);
        Assert.Equal(EventStatus.Active, (await Stored(evt.Id)).Status);
    }

    [Fact]
    public async Task Redeploy_is_forbidden_for_a_member_holding_only_EditEvent()
    {
        var evt = await SeedEvent(x => x.WorkspaceId = Guid.NewGuid());
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ViewEvent, EventPermission.EditEvent]).SeedAsync();
        Factory.OutboundHttp.RespondJson($"http://localhost:4309/api/workspaces/{evt.WorkspaceId}/resources/actions/taint", new { resources = new[] { new { id = "vm", tainted = true } } });

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).PostAsync($"api/events/{evt.Id}/redeploy", null, Ct));

        Assert.Equal(EventStatus.Active, (await Stored(evt.Id)).Status);
    }

    [Fact]
    public async Task Redeploy_is_forbidden_for_a_caller_holding_only_EditEvents()
    {
        var evt = await SeedEvent(x => x.WorkspaceId = Guid.NewGuid());
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditEvents).SeedAsync();
        Factory.OutboundHttp.RespondJson($"http://localhost:4309/api/workspaces/{evt.WorkspaceId}/resources/actions/taint", new { resources = new[] { new { id = "vm", tainted = true } } });

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).PostAsync($"api/events/{evt.Id}/redeploy", null, Ct));

        Assert.Equal(EventStatus.Active, (await Stored(evt.Id)).Status);
    }

    // POST api/events/{id}/invite

    [Fact]
    public async Task Invite_by_the_events_creator_holding_ManageEvent_stores_a_share_code()
    {
        var creatorId = Guid.NewGuid();
        var evt = await SeedEvent(x => x.CreatedBy = creatorId);
        var actor = await Actor().WithId(creatorId).OnEvent(evt.Id, [EventPermission.ManageEvent]).SeedAsync();
        var eventId = evt.Id;

        var invited = await ReadAsync<Event>(await Client(actor).PostAsync($"api/events/{eventId}/invite", null, Ct));

        Assert.NotNull(invited.ShareCode);
        Assert.Equal(invited.ShareCode, (await Stored(eventId)).ShareCode);
    }

    [Fact]
    public async Task Invite_by_the_events_creator_holding_ManageEvents_stores_a_share_code()
    {
        var creatorId = Guid.NewGuid();
        var evt = await SeedEvent(x => x.CreatedBy = creatorId);
        var actor = await Actor().WithId(creatorId).WithSystemPermissions(SystemPermission.ManageEvents).SeedAsync();
        var eventId = evt.Id;

        var invited = await ReadAsync<Event>(await Client(actor).PostAsync($"api/events/{eventId}/invite", null, Ct));

        Assert.NotNull(invited.ShareCode);
        Assert.Equal(invited.ShareCode, (await Stored(eventId)).ShareCode);
    }

    /// <summary>The caller created the event, so the refusal is the ManageEvents check's, not the creator check's.</summary>
    [Fact]
    public async Task Invite_is_forbidden_for_the_events_creator_holding_only_EditEvents()
    {
        var creatorId = Guid.NewGuid();
        var evt = await SeedEvent(x => x.CreatedBy = creatorId);
        var actor = await Actor().WithId(creatorId).WithSystemPermissions(SystemPermission.EditEvents).SeedAsync();

        var problem = await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).PostAsync($"api/events/{evt.Id}/invite", null, Ct));

        Assert.Equal(InsufficientPermissions, problem.Title);
        Assert.Null((await Stored(evt.Id)).ShareCode);
    }

    /// <summary>Only the creator may invite: a caller holding ManageEvents who did not create the event is refused.</summary>
    [Fact]
    public async Task Invite_is_forbidden_for_a_caller_holding_ManageEvents_who_did_not_create_the_event()
    {
        var evt = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEvents).SeedAsync();

        // Data-row gate: the event's CreatedBy, which names another user, is the check that refuses.
        var problem = await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).PostAsync($"api/events/{evt.Id}/invite", null, Ct));

        Assert.Contains("Only owners of an event can create an invite link", problem.Title);
        Assert.Null((await Stored(evt.Id)).ShareCode);
    }

    /// <summary>The caller created the event, so the refusal is the ManageEvent check's, not the creator check's.</summary>
    [Fact]
    public async Task Invite_is_forbidden_for_the_events_creator_holding_only_EditEvent()
    {
        var creatorId = Guid.NewGuid();
        var evt = await SeedEvent(x => x.CreatedBy = creatorId);
        var actor = await Actor().WithId(creatorId).OnEvent(evt.Id, [EventPermission.EditEvent]).SeedAsync();

        var problem = await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).PostAsync($"api/events/{evt.Id}/invite", null, Ct));

        Assert.Equal(InsufficientPermissions, problem.Title);
        Assert.Null((await Stored(evt.Id)).ShareCode);
    }

    // POST api/events/enlist/{code}

    /// <summary>An active event with no view or scenario: enlisting adds the event and template memberships only.</summary>
    [Fact]
    public async Task Enlist_with_a_valid_code_makes_the_caller_a_member_of_the_event_and_an_observer_of_its_template()
    {
        var evt = await SeedEvent(x => x.ShareCode = $"code-{Guid.NewGuid():N}");
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEventTemplates).SeedAsync();

        await AssertStatus(HttpStatusCode.Created, await Client(actor).PostAsync($"api/events/enlist/{evt.ShareCode}", null, Ct));

        await using var context = NewContext();
        Assert.Equal(TestData.EventRoles.Member, (await context.EventMemberships.SingleAsync(x => x.EventId == evt.Id && x.UserId == actor.Id, Ct)).RoleId);
        Assert.Equal(TestData.EventTemplateRoles.Observer,
            (await context.EventTemplateMemberships.SingleAsync(x => x.EventTemplateId == evt.EventTemplateId && x.UserId == actor.Id, Ct)).RoleId);
    }

    [Fact]
    public async Task Enlist_with_an_unknown_code_is_not_found()
    {
        await AssertJsonError(HttpStatusCode.NotFound, await RootClient.PostAsync($"api/events/enlist/code-{Guid.NewGuid():N}", null, Ct));
    }

    [Fact]
    public async Task Enlist_in_an_event_that_has_ended_is_a_conflict()
    {
        var evt = await SeedEvent(x =>
        {
            x.ShareCode = $"code-{Guid.NewGuid():N}";
            x.Status = EventStatus.Ended;
        });

        var problem = await AssertJsonError(HttpStatusCode.Conflict, await RootClient.PostAsync($"api/events/enlist/{evt.ShareCode}", null, Ct));

        Assert.Equal("This event has ended, so it can no longer be joined.", problem.Title);
    }

    // POST api/events/{id}/enlist/{userId}

    [Fact]
    public async Task EnlistUser_by_a_caller_holding_ManageEvents_enlists_and_creates_the_user()
    {
        var evt = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEvents).SeedAsync();
        var userId = Guid.NewGuid();

        var response = await Client(actor).PostAsJsonAsync($"api/events/{evt.Id}/enlist/{userId}", new EnlistUserCommand { UserName = "Enlisted" }, AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        await using var context = NewContext();
        Assert.Equal("Enlisted", (await context.Users.SingleAsync(x => x.Id == userId, Ct)).Name);
        Assert.True(await context.EventMemberships.AnyAsync(x => x.EventId == evt.Id && x.UserId == userId, Ct));
    }

    [Fact]
    public async Task EnlistUser_is_forbidden_for_a_caller_holding_only_ManageEvent_on_the_event()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnEvent(evt.Id, roleId: TestData.EventRoles.Manager).SeedAsync();
        var userId = Guid.NewGuid();

        var response = await Client(actor).PostAsJsonAsync($"api/events/{evt.Id}/enlist/{userId}", new EnlistUserCommand(), AlloyJson, Ct);

        await AssertJsonError(HttpStatusCode.Forbidden, response);
        await using var context = NewContext();
        Assert.False(await context.Users.AnyAsync(x => x.Id == userId, Ct));
    }

    // GET api/events/{id}/virtual-machines, questions; POST qrade

    [Fact]
    public async Task GetVirtualMachines_of_an_event_without_a_workspace_is_empty_for_a_member_holding_ViewEvent()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ViewEvent]).SeedAsync();

        Assert.Empty(await ReadAsync<List<VirtualMachine>>(await Client(actor).GetAsync($"api/events/{evt.Id}/virtual-machines", Ct)));
    }

    [Fact]
    public async Task GetVirtualMachines_of_an_event_without_a_workspace_is_empty_for_a_caller_holding_ViewEvents()
    {
        var evt = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEvents).SeedAsync();

        Assert.Empty(await ReadAsync<List<VirtualMachine>>(await Client(actor).GetAsync($"api/events/{evt.Id}/virtual-machines", Ct)));
    }

    [Fact]
    public async Task GetVirtualMachines_is_forbidden_for_a_caller_holding_ViewEvent_only_on_another_event()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnNewEvent(evt.EventTemplateId, EventPermission.ViewEvent).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/events/{evt.Id}/virtual-machines", Ct));
    }

    [Fact]
    public async Task GetVirtualMachines_is_forbidden_for_a_caller_holding_only_ViewEventTemplates()
    {
        var evt = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEventTemplates).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/events/{evt.Id}/virtual-machines", Ct));
    }

    [Fact]
    public async Task GetQuestions_of_an_event_without_a_workspace_is_empty_for_a_caller_holding_ViewEvents()
    {
        var evt = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEvents).SeedAsync();

        Assert.Empty(await ReadAsync<List<QuestionView>>(await Client(actor).GetAsync($"api/events/{evt.Id}/questions", Ct)));
    }

    [Fact]
    public async Task GetQuestions_of_an_event_without_a_workspace_is_empty_for_a_member_holding_ViewEvent()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ViewEvent]).SeedAsync();

        Assert.Empty(await ReadAsync<List<QuestionView>>(await Client(actor).GetAsync($"api/events/{evt.Id}/questions", Ct)));
    }

    [Fact]
    public async Task GetQuestions_is_forbidden_for_a_caller_holding_only_ViewEventTemplates()
    {
        var evt = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEventTemplates).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/events/{evt.Id}/questions", Ct));
    }

    [Fact]
    public async Task GetQuestions_is_forbidden_for_a_caller_holding_ViewEvent_only_on_another_event()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnNewEvent(evt.EventTemplateId, EventPermission.ViewEvent).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/events/{evt.Id}/questions", Ct));
    }

    [Fact]
    public async Task Grade_of_an_event_without_a_workspace_is_empty_for_a_member_holding_ManageEvent()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ManageEvent]).SeedAsync();

        var graded = await ReadAsync<List<QuestionView>>(await Client(actor).PostAsJsonAsync($"api/events/{evt.Id}/qrade", new[] { "a" }, Ct));

        Assert.Empty(graded);
    }

    [Fact]
    public async Task Grade_of_an_event_without_a_workspace_is_empty_for_a_caller_holding_ManageEvents()
    {
        var evt = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEvents).SeedAsync();

        Assert.Empty(await ReadAsync<List<QuestionView>>(await Client(actor).PostAsJsonAsync($"api/events/{evt.Id}/qrade", new[] { "a" }, Ct)));
    }

    [Fact]
    public async Task Grade_is_forbidden_for_a_member_holding_only_ViewEvent()
    {
        var evt = await SeedEvent();
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ViewEvent]).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync($"api/events/{evt.Id}/qrade", new[] { "a" }, Ct));
    }

    [Fact]
    public async Task Grade_is_forbidden_for_a_caller_holding_only_EditEvents()
    {
        var evt = await SeedEvent();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditEvents).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync($"api/events/{evt.Id}/qrade", new[] { "a" }, Ct));
    }

    // GET api/events/mine

    [Fact]
    public async Task GetMine_returns_the_events_the_caller_is_a_member_of()
    {
        var other = await SeedEvent();
        var actor = await Actor().OnNewEvent(null, EventPermission.ViewEvent).SeedAsync();

        var events = await ReadAsync<List<Event>>(await Client(actor).GetAsync("api/events/mine", Ct));

        Assert.Equal([Assert.Single(actor.NewEvents)], events.Select(x => x.Id));
        Assert.DoesNotContain(other.Id, events.Select(x => x.Id));
    }

    [Theory]
    [InlineData(EventStatus.Ended)]
    [InlineData(EventStatus.Failed)]
    [InlineData(EventStatus.Expired)]
    public async Task GetMine_without_ended_events_leaves_out_a_completed_event(EventStatus completed)
    {
        var actor = await Actor().OnNewEvent(null, EventPermission.ViewEvent).OnNewEvent(null, EventPermission.ViewEvent).SeedAsync();
        await using (var context = NewContext())
        {
            (await context.Events.SingleAsync(x => x.Id == actor.NewEvents[1], Ct)).Status = completed;
            await context.SaveChangesAsync(Ct);
        }

        var events = await ReadAsync<List<Event>>(await Client(actor).GetAsync("api/events/mine?includeEnded=false", Ct));

        Assert.Equal([actor.NewEvents[0]], events.Select(x => x.Id));
    }

    private async Task<EventEntity> SeedEvent(Action<EventEntity> configure = null)
    {
        var template = TestData.EventTemplate();
        var entity = TestData.Event(template.Id);
        configure?.Invoke(entity);
        await Seed(template, entity);

        return entity;
    }

    private async Task<EventEntity> Stored(Guid id)
    {
        await using var context = NewContext();

        return await context.Events.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }
}
