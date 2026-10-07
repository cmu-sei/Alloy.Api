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
using Alloy.Api.Tests.Support;
using Alloy.Api.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Alloy.Api.Tests.Controllers;

/// <summary>
/// <c>EventController</c>'s routes under an event template: launching an event from it (gated on viewing
/// the template, and on managing it to launch for someone else), the resource limits of a basic user, and
/// the template's and a view's event lists.
/// </summary>
public class EventLaunchTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    // POST api/eventTemplates/{eventTemplateId}/events2

    [Fact]
    public async Task Launch_by_a_member_holding_ViewEventTemplate_creates_a_queued_event_the_caller_manages()
    {
        var template = await SeedTemplate();
        var actor = await Actor().WithName("Launcher").OnEventTemplate(template.Id, [EventTemplatePermission.ViewEventTemplate]).SeedAsync();

        var created = await ReadAsync<Event>(await Launch(actor, template.Id, new CreateEventCommand()));

        await using var context = NewContext();
        var saved = await context.Events.Include(x => x.Memberships).SingleAsync(x => x.Id == created.Id, Ct);
        Assert.Equal(($"{template.Name} - Launcher", EventStatus.Creating, InternalEventStatus.LaunchQueued), (saved.Name, saved.Status, saved.InternalStatus));
        Assert.Equal((actor.Id, actor.Id), (saved.UserId, saved.CreatedBy));
        var membership = Assert.Single(saved.Memberships);
        Assert.Equal((actor.Id, TestData.EventRoles.Manager), (membership.UserId, membership.RoleId));
    }

    [Fact]
    public async Task Launch_adds_each_additional_user_as_a_member_and_creates_their_user_rows()
    {
        var template = await SeedTemplate();
        var additional = Guid.NewGuid();

        var created = await ReadAsync<Event>(await Launch(Root, template.Id, new CreateEventCommand { AdditionalUserIds = [additional] }));

        await using var context = NewContext();
        Assert.Equal(TestData.EventRoles.Member, (await context.EventMemberships.SingleAsync(x => x.EventId == created.Id && x.UserId == additional, Ct)).RoleId);
        Assert.True(await context.Users.AnyAsync(x => x.Id == additional, Ct));
    }

    /// <summary>A published template grants every user ViewEventTemplate, so anyone may launch it.</summary>
    [Fact]
    public async Task Launch_of_a_published_template_is_allowed_for_a_caller_with_no_membership_on_it()
    {
        var template = await SeedTemplate(published: true);
        var actor = await Actor().SeedAsync();

        await AssertStatus(HttpStatusCode.Created, await Launch(actor, template.Id, new CreateEventCommand()));
    }

    [Fact]
    public async Task Launch_is_forbidden_for_a_caller_holding_only_ViewEvents()
    {
        var template = await SeedTemplate();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEvents, SystemPermission.CreateEvents).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Launch(actor, template.Id, new CreateEventCommand()));

        await AssertNoEventsOf(template.Id);
    }

    [Fact]
    public async Task Launch_is_forbidden_for_a_caller_holding_ViewEventTemplate_only_on_another_template()
    {
        var template = await SeedTemplate();
        var actor = await Actor().OnNewEventTemplate(EventTemplatePermission.ViewEventTemplate).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Launch(actor, template.Id, new CreateEventCommand()));

        await AssertNoEventsOf(template.Id);
    }

    [Fact]
    public async Task Launch_for_another_user_by_a_caller_holding_ManageEventTemplates_makes_that_user_the_owner()
    {
        var template = await SeedTemplate();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEventTemplates, SystemPermission.ManageEventTemplates).SeedAsync();
        var owner = Guid.NewGuid();

        var created = await ReadAsync<Event>(await Launch(actor, template.Id, new CreateEventCommand { UserId = owner, Username = "Owner" }));

        var saved = await Stored(created.Id);
        Assert.Equal((owner, "Owner"), (saved.UserId, saved.Username));
    }

    [Fact]
    public async Task Launch_for_another_user_by_a_member_holding_ManageEventTemplate_makes_that_user_the_owner()
    {
        var template = await SeedTemplate();
        var actor = await Actor()
            .OnEventTemplate(template.Id, [EventTemplatePermission.ViewEventTemplate, EventTemplatePermission.ManageEventTemplate])
            .SeedAsync();
        var owner = Guid.NewGuid();

        var created = await ReadAsync<Event>(await Launch(actor, template.Id, new CreateEventCommand { UserId = owner, Username = "Owner" }));

        Assert.Equal(owner, (await Stored(created.Id)).UserId);
    }

    [Fact]
    public async Task Launch_for_another_user_is_forbidden_for_a_member_holding_only_ViewEventTemplate_and_EditEventTemplate()
    {
        var template = await SeedTemplate();
        var actor = await Actor()
            .OnEventTemplate(template.Id, [EventTemplatePermission.ViewEventTemplate, EventTemplatePermission.EditEventTemplate])
            .SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Launch(actor, template.Id, new CreateEventCommand { UserId = Guid.NewGuid() }));

        await AssertNoEventsOf(template.Id);
    }

    [Fact]
    public async Task Launch_for_another_user_is_forbidden_for_a_caller_holding_ManageEventTemplate_only_on_another_template()
    {
        var template = await SeedTemplate();
        var actor = await Actor()
            .OnEventTemplate(template.Id, [EventTemplatePermission.ViewEventTemplate])
            .OnNewEventTemplate(EventTemplatePermission.ManageEventTemplate)
            .SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Launch(actor, template.Id, new CreateEventCommand { UserId = Guid.NewGuid() }));
    }

    /// <summary>A basic user's second active event of the same template is answered with a 500.</summary>
    [Fact]
    public async Task Launch_answers_a_second_active_event_of_the_same_template_with_a_server_error()
    {
        var template = await SeedTemplate(published: true);
        var actor = await Actor().SeedAsync();
        await Launch(actor, template.Id, new CreateEventCommand());

        var problem = await AssertJsonError(HttpStatusCode.InternalServerError, await Launch(actor, template.Id, new CreateEventCommand()));

        Assert.Equal($"User {actor.Id} already has an active Event for EventTemplate {template.Id}.", problem.Detail);
        await using var context = NewContext();
        Assert.Equal(1, await context.Events.CountAsync(x => x.EventTemplateId == template.Id, Ct));
    }

    /// <summary>Resource:MaxEventsForBasicUser is 2 as shipped; the third is answered with a 500.</summary>
    // Same case as Launch_answers_a_second_active_event_of_the_same_template_with_a_server_error.
    [Fact]
    public async Task Launch_answers_a_basic_users_third_active_event_with_a_server_error()
    {
        var first = await SeedTemplate(published: true);
        var second = await SeedTemplate(published: true);
        var third = await SeedTemplate(published: true);
        var actor = await Actor().SeedAsync();
        await Launch(actor, first.Id, new CreateEventCommand());
        await Launch(actor, second.Id, new CreateEventCommand());

        var problem = await AssertJsonError(HttpStatusCode.InternalServerError, await Launch(actor, third.Id, new CreateEventCommand()));

        Assert.Equal($"User {actor.Id} already has 2 Events active.", problem.Detail);
    }

    [Fact]
    public async Task Launch_by_a_caller_holding_ManageEvents_is_not_limited()
    {
        var template = await SeedTemplate();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEventTemplates, SystemPermission.ManageEvents).SeedAsync();
        await Launch(actor, template.Id, new CreateEventCommand());

        await AssertStatus(HttpStatusCode.Created, await Launch(actor, template.Id, new CreateEventCommand()));
    }

    [Fact]
    public async Task Launch_of_an_unknown_template_is_not_found()
    {
        await AssertJsonError(HttpStatusCode.NotFound, await Launch(Root, Guid.NewGuid(), new CreateEventCommand()));
    }

    // POST api/eventTemplates/{eventTemplateId}/events (legacy)

    [Fact]
    public async Task LegacyLaunch_by_a_member_holding_ViewEventTemplate_creates_the_event()
    {
        var template = await SeedTemplate();
        var actor = await Actor().OnEventTemplate(template.Id, [EventTemplatePermission.ViewEventTemplate]).SeedAsync();

        var created = await ReadAsync<Event>(await Client(actor).PostAsync($"api/eventTemplates/{template.Id}/events", null, Ct));

        Assert.Equal(actor.Id, (await Stored(created.Id)).UserId);
    }

    [Fact]
    public async Task LegacyLaunch_is_forbidden_for_a_caller_holding_ViewEventTemplate_only_on_another_template()
    {
        var template = await SeedTemplate();
        var actor = await Actor().OnNewEventTemplate(EventTemplatePermission.ViewEventTemplate).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).PostAsync($"api/eventTemplates/{template.Id}/events", null, Ct));

        await AssertNoEventsOf(template.Id);
    }

    [Fact]
    public async Task LegacyLaunch_for_another_user_is_forbidden_for_a_member_holding_only_ViewEventTemplate()
    {
        var template = await SeedTemplate();
        var actor = await Actor().OnEventTemplate(template.Id, [EventTemplatePermission.ViewEventTemplate]).SeedAsync();

        var response = await Client(actor).PostAsync($"api/eventTemplates/{template.Id}/events?userId={Guid.NewGuid()}&username=Other", null, Ct);

        await AssertJsonError(HttpStatusCode.Forbidden, response);
        await AssertNoEventsOf(template.Id);
    }

    // GET api/eventTemplates/{eventTemplateId}/events

    [Fact]
    public async Task GetTemplateEvents_returns_the_templates_events_to_a_member_holding_ManageEventTemplate()
    {
        var template = await SeedTemplate();
        var evt = TestData.Event(template.Id);
        var unrelated = TestData.Event(null);
        await Seed(evt, unrelated);
        var actor = await Actor().OnEventTemplate(template.Id, [EventTemplatePermission.ManageEventTemplate]).SeedAsync();

        var events = await ReadAsync<List<Event>>(await Client(actor).GetAsync($"api/eventTemplates/{template.Id}/events", Ct));

        Assert.Equal([evt.Id], events.Select(x => x.Id));
    }

    [Fact]
    public async Task GetTemplateEvents_returns_the_templates_events_to_a_caller_holding_ViewEvents()
    {
        var template = await SeedTemplate();
        var evt = TestData.Event(template.Id);
        await Seed(evt);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEvents).SeedAsync();

        var events = await ReadAsync<List<Event>>(await Client(actor).GetAsync($"api/eventTemplates/{template.Id}/events", Ct));

        Assert.Equal([evt.Id], events.Select(x => x.Id));
    }

    [Fact]
    public async Task GetTemplateEvents_is_forbidden_for_a_member_holding_only_EditEventTemplate()
    {
        var template = await SeedTemplate();
        var actor = await Actor()
            .OnEventTemplate(template.Id, [EventTemplatePermission.ViewEventTemplate, EventTemplatePermission.EditEventTemplate])
            .SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/eventTemplates/{template.Id}/events", Ct));
    }

    [Fact]
    public async Task GetTemplateEvents_is_forbidden_for_a_caller_holding_only_ViewEventTemplates()
    {
        var template = await SeedTemplate();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEventTemplates).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/eventTemplates/{template.Id}/events", Ct));
    }

    [Fact]
    public async Task GetTemplateEvents_is_forbidden_for_a_caller_holding_ManageEventTemplate_only_on_another_template()
    {
        var template = await SeedTemplate();
        var actor = await Actor().OnNewEventTemplate(EventTemplatePermission.ManageEventTemplate).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/eventTemplates/{template.Id}/events", Ct));
    }

    // GET api/eventTemplates/{eventTemplateId}/events/mine

    [Fact]
    public async Task GetMyTemplateEvents_returns_only_the_callers_own_events_of_the_template()
    {
        var template = await SeedTemplate();
        var actorId = Guid.NewGuid();
        var mine = TestData.Event(template.Id, userId: actorId);
        var theirs = TestData.Event(template.Id);
        await Seed(mine, theirs);
        var actor = await Actor().WithId(actorId).SeedAsync();

        var events = await ReadAsync<List<Event>>(await Client(actor).GetAsync($"api/eventTemplates/{template.Id}/events/mine", Ct));

        Assert.Equal([mine.Id], events.Select(x => x.Id));
    }

    [Fact]
    public async Task GetMyTemplateEvents_with_invites_includes_events_the_caller_was_enlisted_in()
    {
        var template = await SeedTemplate();
        var actor = await Actor().OnNewEvent(template.Id, EventPermission.ViewEvent).SeedAsync();

        var events = await ReadAsync<List<Event>>(await Client(actor).GetAsync($"api/eventTemplates/{template.Id}/events/mine?includeInvites=true", Ct));

        Assert.Equal(actor.NewEvents, events.Select(x => x.Id));
    }

    // GET api/views/{viewId}/events/mine

    /// <summary>The view in the route is not bound: the caller's own event on that view is not returned.</summary>
    [Fact]
    public async Task GetMyViewEvents_does_not_read_the_view_id_from_the_route()
    {
        var (actor, owned, viewId) = await SeedOwnerOfAnEventOnAView();

        var events = await ReadAsync<List<Event>>(await Client(actor).GetAsync($"api/views/{viewId}/events/mine", Ct));

        Assert.DoesNotContain(owned, events.Select(x => x.Id));
    }

    /// <summary>With the view passed as playerViewId, the list holds every event the caller is a member of, of any view.</summary>
    [Fact]
    public async Task GetMyViewEvents_returns_the_callers_memberships_on_every_view()
    {
        var viewId = Guid.NewGuid();
        var onTheView = TestData.Event(null);
        onTheView.ViewId = viewId;
        var onAnotherView = TestData.Event(null);
        onAnotherView.ViewId = Guid.NewGuid();
        await Seed(onTheView, onAnotherView);
        var actor = await Actor()
            .OnEvent(onTheView.Id, [EventPermission.ViewEvent])
            .OnEvent(onAnotherView.Id, [EventPermission.ViewEvent])
            .SeedAsync();

        var events = await ReadAsync<List<Event>>(await Client(actor).GetAsync($"api/views/{viewId}/events/mine?playerViewId={viewId}", Ct));

        Assert.Equal(new[] { onTheView.Id, onAnotherView.Id }.Order(), events.Select(x => x.Id).Order());
    }

    private Task<HttpResponseMessage> Launch(TestActor actor, Guid templateId, CreateEventCommand command) =>
        Client(actor).PostAsJsonAsync($"api/eventTemplates/{templateId}/events2", command, AlloyJson, Ct);

    private async Task<EventTemplateEntity> SeedTemplate(bool published = false)
    {
        var template = TestData.EventTemplate($"Template {Guid.NewGuid():N}", published: published);
        await Seed(template);

        return template;
    }

    /// <summary>
    /// An event on a view, owned by the actor (<c>Event.UserId</c>) who has no membership of their own on
    /// it: <c>GetMyViewEventsAsync</c> finds it only through the view and the owner, so only through the route.
    /// </summary>
    private async Task<(TestActor Actor, Guid EventId, Guid ViewId)> SeedOwnerOfAnEventOnAView()
    {
        var actorId = Guid.NewGuid();
        var viewId = Guid.NewGuid();
        var owned = TestData.Event(null, userId: actorId);
        owned.ViewId = viewId;
        var other = TestData.User();
        await Seed(owned, other, TestData.EventMembership(owned.Id, TestData.EventRoles.Member, other.Id));

        return (await Actor().WithId(actorId).SeedAsync(), owned.Id, viewId);
    }

    private async Task AssertNoEventsOf(Guid templateId)
    {
        await using var context = NewContext();
        Assert.False(await context.Events.AnyAsync(x => x.EventTemplateId == templateId, Ct));
    }

    private async Task<EventEntity> Stored(Guid id)
    {
        await using var context = NewContext();

        return await context.Events.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }
}
