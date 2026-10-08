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
/// <c>EventTemplateController</c> over HTTP: each route's gate (a system permission, or the template
/// permission on the template the route names), what a write stores, and what it broadcasts. Templates
/// here have no view, so Player is never asked (EventTemplateViewValidationTests covers the view).
/// </summary>
public class EventTemplateControllerTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    // GET api/eventTemplates

    [Fact]
    public async Task GetAll_returns_every_template_to_a_caller_holding_ViewEventTemplates()
    {
        var template = await SeedTemplate();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEventTemplates).SeedAsync();

        var templates = await ReadAsync<List<EventTemplate>>(await Client(actor).GetAsync("api/eventTemplates", Ct));

        Assert.Contains(template.Id, templates.Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_returns_member_and_published_templates_to_a_caller_holding_only_ViewEvents()
    {
        var hidden = await SeedTemplate();
        var published = await SeedTemplate(published: true);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEvents)
            .OnNewEventTemplate(EventTemplatePermission.ViewEventTemplate).SeedAsync();

        var templates = await ReadAsync<List<EventTemplate>>(await Client(actor).GetAsync("api/eventTemplates", Ct));

        Assert.Equal(new[] { Assert.Single(actor.NewEventTemplates), published.Id }.Order(), templates.Select(x => x.Id).Order());
        Assert.DoesNotContain(hidden.Id, templates.Select(x => x.Id));
    }

    /// <summary>Each template carries the caller's permissions on that template only, with its system permissions.</summary>
    [Fact]
    public async Task GetAll_adds_the_callers_template_and_system_permissions_to_each_template()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEvents)
            .OnNewEventTemplate(EventTemplatePermission.ViewEventTemplate, EventTemplatePermission.EditEventTemplate)
            .OnNewEventTemplate(EventTemplatePermission.ViewEventTemplate, EventTemplatePermission.ManageEventTemplate)
            .SeedAsync();

        var templates = await ReadAsync<List<EventTemplate>>(await Client(actor).GetAsync("api/eventTemplates", Ct));

        var permissions = templates.Single(x => x.Id == actor.NewEventTemplates[0]).EventTemplatePermissions.ToList();
        Assert.Contains(nameof(SystemPermission.ViewEvents), permissions);
        Assert.Contains(permissions, x => x.Split(',').Contains(nameof(EventTemplatePermission.EditEventTemplate)));
        Assert.DoesNotContain(permissions, x => x.Split(',').Contains(nameof(EventTemplatePermission.ManageEventTemplate)));
    }

    // GET api/eventTemplates/{id}

    [Fact]
    public async Task Get_returns_the_template_to_a_member_holding_ViewEventTemplate()
    {
        var template = await SeedTemplate();
        var actor = await Actor().OnEventTemplate(template.Id, [EventTemplatePermission.ViewEventTemplate]).SeedAsync();

        var read = await ReadAsync<EventTemplate>(await Client(actor).GetAsync($"api/eventTemplates/{template.Id}", Ct));

        Assert.Equal(template.Name, read.Name);
    }

    [Fact]
    public async Task Get_returns_a_published_template_to_a_caller_with_no_membership_on_it()
    {
        var template = await SeedTemplate(published: true);
        var actor = await Actor().SeedAsync();

        await AssertStatus(HttpStatusCode.OK, await Client(actor).GetAsync($"api/eventTemplates/{template.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_only_ViewEvents()
    {
        var template = await SeedTemplate();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEvents).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/eventTemplates/{template.Id}", Ct));
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_ViewEventTemplate_only_on_another_template()
    {
        var template = await SeedTemplate();
        var actor = await Actor().OnNewEventTemplate(EventTemplatePermission.ViewEventTemplate).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/eventTemplates/{template.Id}", Ct));
    }

    /// <summary>A template that does not exist is answered with a 403 even for a caller holding every permission.</summary>
    [Fact]
    public async Task Get_answers_an_unknown_template_with_forbidden()
    {
        await AssertJsonError(HttpStatusCode.Forbidden, await RootClient.GetAsync($"api/eventTemplates/{Guid.NewGuid()}", Ct));
    }

    // POST api/eventTemplates

    [Fact]
    public async Task Create_by_a_caller_holding_CreateEventTemplates_stores_the_template_as_theirs()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.CreateEventTemplates).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync("api/eventTemplates", Template("Created"), AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        await using var context = NewContext();
        var saved = await context.EventTemplates.SingleAsync(x => x.Name == "Created", Ct);
        Assert.Equal((actor.Id, 2), (saved.CreatedBy, saved.DurationHours));
    }

    /// <summary>The creator of a template gets no membership on it.</summary>
    [Fact]
    public async Task Create_gives_the_creator_no_membership_on_the_new_template()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.CreateEventTemplates).SeedAsync();

        var created = await ReadAsync<EventTemplate>(await Client(actor).PostAsJsonAsync("api/eventTemplates", Template("Unowned"), AlloyJson, Ct));

        await using var context = NewContext();
        Assert.False(await context.EventTemplateMemberships.AnyAsync(x => x.EventTemplateId == created.Id, Ct));
    }

    [Fact]
    public async Task Create_broadcasts_the_new_template_to_its_group_and_the_admin_group()
    {
        var created = await ReadAsync<EventTemplate>(await RootClient.PostAsJsonAsync("api/eventTemplates", Template("Broadcast"), AlloyJson, Ct));

        var broadcast = Assert.Single(Factory.Hub.ToGroup(created.Id));
        Assert.Equal(EngineHubMethods.EventTemplateCreated, broadcast.Method);
        Assert.Single(Factory.Hub.ToGroups(created.Id.ToString(), EngineHub.ADMIN_EVENT_TEMPLATE_GROUP));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_only_EditEventTemplates()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditEventTemplates, SystemPermission.CreateEvents).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).PostAsJsonAsync("api/eventTemplates", Template("Denied"), AlloyJson, Ct));

        await using var context = NewContext();
        Assert.False(await context.EventTemplates.AnyAsync(x => x.Name == "Denied", Ct));
    }

    // PUT api/eventTemplates/{id}

    [Fact]
    public async Task Update_by_a_member_holding_EditEventTemplate_stores_the_change()
    {
        var template = await SeedTemplate();
        var actor = await Actor().OnEventTemplate(template.Id, [EventTemplatePermission.EditEventTemplate]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/eventTemplates/{template.Id}", Template("Renamed", template.Id), AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var saved = await Stored(template.Id);
        Assert.Equal(("Renamed", actor.Id), (saved.Name, saved.ModifiedBy));
    }

    [Fact]
    public async Task Update_by_a_caller_holding_EditEventTemplates_stores_the_change()
    {
        var template = await SeedTemplate();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditEventTemplates).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/eventTemplates/{template.Id}", Template("Renamed", template.Id), AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("Renamed", (await Stored(template.Id)).Name);
    }

    [Fact]
    public async Task Update_broadcasts_the_modified_properties()
    {
        var template = await SeedTemplate();

        await RootClient.PutAsJsonAsync($"api/eventTemplates/{template.Id}", Template("Renamed", template.Id), AlloyJson, Ct);

        var broadcast = Assert.Single(Factory.Hub.ToGroup(template.Id));
        Assert.Equal(EngineHubMethods.EventTemplateUpdated, broadcast.Method);
        Assert.Contains("name", Assert.IsType<string[]>(broadcast.Arguments[1]));
    }

    /// <summary>The body's id is mapped onto the stored row, so an id other than the route's is answered with a 500.</summary>
    [Fact]
    public async Task Update_answers_a_body_id_other_than_the_routes_with_a_server_error()
    {
        var template = await SeedTemplate();

        var response = await RootClient.PutAsJsonAsync($"api/eventTemplates/{template.Id}", Template("Renamed", Guid.NewGuid()), AlloyJson, Ct);

        var problem = await AssertJsonError(HttpStatusCode.InternalServerError, response);
        Assert.StartsWith("The property 'EventTemplateEntity.Id' is part of a key", problem.Detail);
        Assert.Equal(template.Name, (await Stored(template.Id)).Name);
    }

    [Fact]
    public async Task Update_is_forbidden_for_a_member_holding_only_ViewEventTemplate()
    {
        var template = await SeedTemplate();
        var actor = await Actor().OnEventTemplate(template.Id, [EventTemplatePermission.ViewEventTemplate]).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden,
            await Client(actor).PutAsJsonAsync($"api/eventTemplates/{template.Id}", Template("Renamed", template.Id), AlloyJson, Ct));

        Assert.Equal(template.Name, (await Stored(template.Id)).Name);
    }

    [Fact]
    public async Task Update_is_forbidden_for_a_caller_holding_EditEventTemplate_only_on_another_template()
    {
        var template = await SeedTemplate();
        var actor = await Actor().OnNewEventTemplate(EventTemplatePermission.EditEventTemplate).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden,
            await Client(actor).PutAsJsonAsync($"api/eventTemplates/{template.Id}", Template("Renamed", template.Id), AlloyJson, Ct));
    }

    [Fact]
    public async Task Update_is_forbidden_for_a_caller_holding_only_CreateEventTemplates()
    {
        var template = await SeedTemplate();
        var actor = await Actor().WithSystemPermissions(SystemPermission.CreateEventTemplates, SystemPermission.ViewEventTemplates).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden,
            await Client(actor).PutAsJsonAsync($"api/eventTemplates/{template.Id}", Template("Renamed", template.Id), AlloyJson, Ct));
    }

    // DELETE api/eventTemplates/{id}

    [Fact]
    public async Task Delete_by_a_member_holding_ManageEventTemplate_removes_the_template_and_its_memberships()
    {
        var template = await SeedTemplate();
        var actor = await Actor().OnEventTemplate(template.Id, [EventTemplatePermission.ManageEventTemplate]).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/eventTemplates/{template.Id}", Ct));

        await using var context = NewContext();
        Assert.False(await context.EventTemplates.AnyAsync(x => x.Id == template.Id, Ct));
        Assert.False(await context.EventTemplateMemberships.AnyAsync(x => x.EventTemplateId == template.Id, Ct));
    }

    [Fact]
    public async Task Delete_by_a_caller_holding_ManageEventTemplates_removes_the_template()
    {
        var template = await SeedTemplate();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEventTemplates).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/eventTemplates/{template.Id}", Ct));

        await using var context = NewContext();
        Assert.False(await context.EventTemplates.AnyAsync(x => x.Id == template.Id, Ct));
    }

    /// <summary>A template's deletion is broadcast as EventDeleted, carrying the template's id.</summary>
    [Fact]
    public async Task Delete_broadcasts_the_template_deletion_as_EventDeleted()
    {
        var template = await SeedTemplate();

        await RootClient.DeleteAsync($"api/eventTemplates/{template.Id}", Ct);

        var broadcast = Assert.Single(Factory.Hub.ToGroup(template.Id));
        Assert.Equal(EngineHubMethods.EventDeleted, broadcast.Method);
        Assert.Equal(template.Id, broadcast.Argument);
    }

    /// <summary>A template that events were launched from cannot be deleted; the foreign key refusal is answered with a 500.</summary>
    [Fact]
    public async Task Delete_answers_a_template_with_events_with_a_server_error()
    {
        var template = await SeedTemplate();
        await Seed(TestData.Event(template.Id));

        var problem = await AssertJsonError(HttpStatusCode.InternalServerError, await RootClient.DeleteAsync($"api/eventTemplates/{template.Id}", Ct));
        Assert.Equal("An error occurred while saving the entity changes. See the inner exception for details.", problem.Detail);

        await using var context = NewContext();
        Assert.True(await context.EventTemplates.AnyAsync(x => x.Id == template.Id, Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_member_holding_only_EditEventTemplate()
    {
        var template = await SeedTemplate();
        var actor = await Actor()
            .OnEventTemplate(template.Id, [EventTemplatePermission.ViewEventTemplate, EventTemplatePermission.EditEventTemplate])
            .SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).DeleteAsync($"api/eventTemplates/{template.Id}", Ct));

        await using var context = NewContext();
        Assert.True(await context.EventTemplates.AnyAsync(x => x.Id == template.Id, Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_EditEventTemplates()
    {
        var template = await SeedTemplate();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditEventTemplates).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).DeleteAsync($"api/eventTemplates/{template.Id}", Ct));
    }

    private static EventTemplate Template(string name, Guid? id = null) =>
        new() { Id = id ?? Guid.Empty, Name = name, Description = "Description", DurationHours = 2 };

    private async Task<EventTemplateEntity> SeedTemplate(bool published = false)
    {
        var template = TestData.EventTemplate($"Template {Guid.NewGuid():N}", published: published);
        await Seed(template);

        return template;
    }

    private async Task<EventTemplateEntity> Stored(Guid id)
    {
        await using var context = NewContext();

        return await context.EventTemplates.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }
}
