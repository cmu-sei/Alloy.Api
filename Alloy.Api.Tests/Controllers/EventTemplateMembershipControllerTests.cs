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
/// <c>EventTemplateMembershipController</c> over HTTP: reading needs ViewEventTemplate and writing
/// ManageEventTemplate on the membership's template (or the matching system permission).
/// </summary>
public class EventTemplateMembershipControllerTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    // GET api/eventTemplates/memberships/{id}

    [Fact]
    public async Task Get_returns_the_membership_to_a_member_holding_ViewEventTemplate_on_its_template()
    {
        var (template, membership) = await SeedMembership();
        var actor = await Actor().OnEventTemplate(template.Id, [EventTemplatePermission.ViewEventTemplate]).SeedAsync();

        var read = await ReadAsync<EventTemplateMembership>(await Client(actor).GetAsync($"api/eventTemplates/memberships/{membership.Id}", Ct));

        Assert.Equal((template.Id, membership.UserId), (read.EventTemplateId, read.UserId));
    }

    [Fact]
    public async Task Get_returns_the_membership_to_a_caller_holding_ViewEventTemplates()
    {
        var (_, membership) = await SeedMembership();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEventTemplates).SeedAsync();

        var read = await ReadAsync<EventTemplateMembership>(await Client(actor).GetAsync($"api/eventTemplates/memberships/{membership.Id}", Ct));

        Assert.Equal(membership.Id, read.Id);
    }

    [Fact]
    public async Task Get_is_forbidden_for_a_caller_holding_ViewEventTemplate_only_on_another_template()
    {
        var (_, membership) = await SeedMembership();
        var actor = await Actor().OnNewEventTemplate(EventTemplatePermission.ViewEventTemplate).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/eventTemplates/memberships/{membership.Id}", Ct));
    }

    // GET api/eventTemplates/{id}/memberships

    [Fact]
    public async Task GetAll_returns_the_templates_memberships_to_a_caller_holding_ViewEventTemplates()
    {
        var (template, membership) = await SeedMembership();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEventTemplates).SeedAsync();

        var memberships = await ReadAsync<List<EventTemplateMembership>>(await Client(actor).GetAsync($"api/eventTemplates/{template.Id}/memberships", Ct));

        Assert.Equal([membership.Id], memberships.Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_returns_the_templates_memberships_to_a_member_holding_ViewEventTemplate()
    {
        var (template, membership) = await SeedMembership();
        var actor = await Actor().OnEventTemplate(template.Id, [EventTemplatePermission.ViewEventTemplate]).SeedAsync();

        var memberships = await ReadAsync<List<EventTemplateMembership>>(await Client(actor).GetAsync($"api/eventTemplates/{template.Id}/memberships", Ct));

        Assert.Contains(membership.Id, memberships.Select(x => x.Id));
    }

    [Fact]
    public async Task GetAll_is_forbidden_for_a_caller_holding_only_ViewEvents()
    {
        var (template, _) = await SeedMembership();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEvents).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync($"api/eventTemplates/{template.Id}/memberships", Ct));
    }

    // POST api/eventTemplates/{eventTemplateId}/memberships

    [Fact]
    public async Task Create_by_a_member_holding_ManageEventTemplate_stores_the_membership()
    {
        var template = await SeedTemplate();
        var user = TestData.User();
        await Seed(user);
        var actor = await Actor().OnEventTemplate(template.Id, [EventTemplatePermission.ManageEventTemplate]).SeedAsync();

        var created = await ReadAsync<EventTemplateMembership>(await Client(actor).PostAsJsonAsync($"api/eventTemplates/{template.Id}/memberships",
            new EventTemplateMembership { EventTemplateId = template.Id, UserId = user.Id, RoleId = TestData.EventTemplateRoles.Observer }, AlloyJson, Ct));

        await using var context = NewContext();
        var saved = await context.EventTemplateMemberships.SingleAsync(x => x.Id == created.Id, Ct);
        Assert.Equal((template.Id, user.Id, TestData.EventTemplateRoles.Observer), (saved.EventTemplateId, saved.UserId, saved.RoleId));
    }

    [Fact]
    public async Task Create_by_a_caller_holding_ManageEventTemplates_stores_the_membership()
    {
        var template = await SeedTemplate();
        var user = TestData.User();
        await Seed(user);
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEventTemplates).SeedAsync();

        var created = await ReadAsync<EventTemplateMembership>(await Client(actor).PostAsJsonAsync($"api/eventTemplates/{template.Id}/memberships",
            new EventTemplateMembership { EventTemplateId = template.Id, UserId = user.Id, RoleId = TestData.EventTemplateRoles.Member }, AlloyJson, Ct));

        Assert.Equal(user.Id, (await StoredMembership(created.Id)).UserId);
    }

    /// <summary>A caller holding only ManageEventTemplate adds a membership with the all-permissions Manager role.</summary>
    [Fact]
    public async Task Create_stores_a_Manager_membership_added_by_a_caller_holding_only_ManageEventTemplate()
    {
        var template = await SeedTemplate();
        var user = TestData.User();
        await Seed(user);
        var actor = await Actor().OnEventTemplate(template.Id, [EventTemplatePermission.ManageEventTemplate]).SeedAsync();

        var created = await ReadAsync<EventTemplateMembership>(await Client(actor).PostAsJsonAsync($"api/eventTemplates/{template.Id}/memberships",
            new EventTemplateMembership { EventTemplateId = template.Id, UserId = user.Id, RoleId = TestData.EventTemplateRoles.Manager }, AlloyJson, Ct));

        Assert.Equal(TestData.EventTemplateRoles.Manager, (await StoredMembership(created.Id)).RoleId);
    }

    /// <summary>A new membership is broadcast to the group named for the membership's own id.</summary>
    [Fact]
    public async Task Create_broadcasts_the_membership_to_the_group_of_its_own_id_and_not_its_templates()
    {
        var template = await SeedTemplate();
        var user = TestData.User();
        await Seed(user);

        var created = await ReadAsync<EventTemplateMembership>(await RootClient.PostAsJsonAsync($"api/eventTemplates/{template.Id}/memberships",
            new EventTemplateMembership { EventTemplateId = template.Id, UserId = user.Id, RoleId = TestData.EventTemplateRoles.Member }, AlloyJson, Ct));

        Assert.Equal(EngineHubMethods.EventTemplateMembershipCreated, Assert.Single(Factory.Hub.ToGroup(created.Id)).Method);
        Assert.Empty(Factory.Hub.ToGroup(template.Id));
    }

    /// <summary>A body naming another template than the route is refused with a <c>System.Data.DataException</c>, answered with a 500.</summary>
    [Fact]
    public async Task Create_answers_a_body_template_other_than_the_routes_with_a_server_error()
    {
        var template = await SeedTemplate();
        var other = await SeedTemplate();

        var response = await RootClient.PostAsJsonAsync($"api/eventTemplates/{template.Id}/memberships",
            new EventTemplateMembership { EventTemplateId = other.Id, UserId = Root.Id, RoleId = TestData.EventTemplateRoles.Member }, AlloyJson, Ct);

        var problem = await AssertJsonError(HttpStatusCode.InternalServerError, response);
        Assert.Equal("The EventTemplateId of the membership must match the EventTemplateId of the URL.", problem.Detail);
    }

    /// <summary>The unique index on (template, user, group) does not hold while the group is null, so a user can be added twice.</summary>
    [Fact]
    public async Task Create_stores_a_second_membership_of_the_same_user_on_the_same_template()
    {
        var (template, membership) = await SeedMembership();

        var response = await RootClient.PostAsJsonAsync($"api/eventTemplates/{template.Id}/memberships",
            new EventTemplateMembership { EventTemplateId = template.Id, UserId = membership.UserId, RoleId = TestData.EventTemplateRoles.Member }, AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        await using var context = NewContext();
        Assert.Equal(2, await context.EventTemplateMemberships.CountAsync(x => x.EventTemplateId == template.Id && x.UserId == membership.UserId, Ct));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_member_holding_only_EditEventTemplate()
    {
        var template = await SeedTemplate();
        var actor = await Actor()
            .OnEventTemplate(template.Id, [EventTemplatePermission.ViewEventTemplate, EventTemplatePermission.EditEventTemplate])
            .SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/eventTemplates/{template.Id}/memberships",
            new EventTemplateMembership { EventTemplateId = template.Id, UserId = actor.Id, RoleId = TestData.EventTemplateRoles.Manager }, AlloyJson, Ct);

        await AssertJsonError(HttpStatusCode.Forbidden, response);
        await using var context = NewContext();
        Assert.False(await context.EventTemplateMemberships.AnyAsync(x => x.EventTemplateId == template.Id && x.RoleId == TestData.EventTemplateRoles.Manager, Ct));
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_caller_holding_ManageEventTemplate_only_on_another_template()
    {
        var template = await SeedTemplate();
        var actor = await Actor().OnNewEventTemplate(EventTemplatePermission.ManageEventTemplate).SeedAsync();

        var response = await Client(actor).PostAsJsonAsync($"api/eventTemplates/{template.Id}/memberships",
            new EventTemplateMembership { EventTemplateId = template.Id, UserId = actor.Id, RoleId = TestData.EventTemplateRoles.Manager }, AlloyJson, Ct);

        await AssertJsonError(HttpStatusCode.Forbidden, response);
    }

    // PUT api/EventTemplates/Memberships/{id}

    [Fact]
    public async Task Update_by_a_member_holding_ManageEventTemplate_changes_the_role()
    {
        var (template, membership) = await SeedMembership();
        var actor = await Actor().OnEventTemplate(template.Id, [EventTemplatePermission.ManageEventTemplate]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/EventTemplates/Memberships/{membership.Id}",
            new EventTemplateMembership { Id = membership.Id, EventTemplateId = template.Id, RoleId = TestData.EventTemplateRoles.Manager }, AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(TestData.EventTemplateRoles.Manager, (await StoredMembership(membership.Id)).RoleId);
    }

    [Fact]
    public async Task Update_by_a_caller_holding_ManageEventTemplates_changes_the_role()
    {
        var (template, membership) = await SeedMembership();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEventTemplates).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/EventTemplates/Memberships/{membership.Id}",
            new EventTemplateMembership { Id = membership.Id, EventTemplateId = template.Id, RoleId = TestData.EventTemplateRoles.Observer }, AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(TestData.EventTemplateRoles.Observer, (await StoredMembership(membership.Id)).RoleId);
    }

    /// <summary>The gate reads the stored membership's template, so a body naming the caller's own template does not help.</summary>
    [Fact]
    public async Task Update_is_forbidden_for_a_caller_managing_only_the_template_named_in_the_body()
    {
        var (_, membership) = await SeedMembership();
        var actor = await Actor().OnNewEventTemplate(EventTemplatePermission.ManageEventTemplate).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/EventTemplates/Memberships/{membership.Id}",
            new EventTemplateMembership { Id = membership.Id, EventTemplateId = Assert.Single(actor.NewEventTemplates), RoleId = TestData.EventTemplateRoles.Manager }, AlloyJson, Ct);

        await AssertJsonError(HttpStatusCode.Forbidden, response);
        Assert.Equal(TestData.EventTemplateRoles.Member, (await StoredMembership(membership.Id)).RoleId);
    }

    // DELETE api/eventTemplates/memberships/{id}

    // Same case as Create_broadcasts_the_membership_to_the_group_of_its_own_id_and_not_its_templates.
    [Fact]
    public async Task Delete_by_a_caller_holding_ManageEventTemplates_removes_the_membership_and_broadcasts_it()
    {
        var (_, membership) = await SeedMembership();
        var actor = await Actor().WithSystemPermissions(SystemPermission.ManageEventTemplates).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/eventTemplates/memberships/{membership.Id}", Ct));

        await using var context = NewContext();
        Assert.False(await context.EventTemplateMemberships.AnyAsync(x => x.Id == membership.Id, Ct));
        Assert.Equal(EngineHubMethods.EventTemplateMembershipDeleted, Assert.Single(Factory.Hub.ToGroup(membership.Id)).Method);
    }

    [Fact]
    public async Task Delete_by_a_member_holding_ManageEventTemplate_removes_the_membership()
    {
        var (template, membership) = await SeedMembership();
        var actor = await Actor().OnEventTemplate(template.Id, [EventTemplatePermission.ManageEventTemplate]).SeedAsync();

        await AssertStatus(HttpStatusCode.NoContent, await Client(actor).DeleteAsync($"api/eventTemplates/memberships/{membership.Id}", Ct));

        await using var context = NewContext();
        Assert.False(await context.EventTemplateMemberships.AnyAsync(x => x.Id == membership.Id, Ct));
    }

    [Fact]
    public async Task Delete_is_forbidden_for_a_caller_holding_only_EditEventTemplates()
    {
        var (_, membership) = await SeedMembership();
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditEventTemplates).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).DeleteAsync($"api/eventTemplates/memberships/{membership.Id}", Ct));

        await using var context = NewContext();
        Assert.True(await context.EventTemplateMemberships.AnyAsync(x => x.Id == membership.Id, Ct));
    }

    private async Task<EventTemplateEntity> SeedTemplate()
    {
        var template = TestData.EventTemplate($"Template {Guid.NewGuid():N}");
        await Seed(template);

        return template;
    }

    private async Task<(EventTemplateEntity Template, EventTemplateMembershipEntity Membership)> SeedMembership()
    {
        var template = await SeedTemplate();
        var user = TestData.User();
        var membership = TestData.EventTemplateMembership(template.Id, TestData.EventTemplateRoles.Member, user.Id);
        await Seed(user, membership);

        return (template, membership);
    }

    private async Task<EventTemplateMembershipEntity> StoredMembership(Guid id)
    {
        await using var context = NewContext();

        return await context.EventTemplateMemberships.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }
}
