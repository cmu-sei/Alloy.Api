// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Infrastructure.Authorization;
using Alloy.Api.Tests.Support;
using Alloy.Api.ViewModels;

namespace Alloy.Api.Tests.Infrastructure.Authorization;

/// <summary>
/// <c>AuthorizationService</c>, Alloy's own, over the real handlers and the test's database, on principals
/// in the claim shapes <c>UserClaimsService</c> produces. A passing system check skips every scoped check,
/// so an empty or missing permission list must grant nothing; a scoped check resolves a membership to the
/// resource it belongs to through the stored row.
/// </summary>
public class AuthorizationServiceTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task AuthorizeAsync_refuses_an_empty_system_permission_list()
    {
        var service = Service(ClaimsPrincipalBuilder.Anonymous());

        Assert.False(await service.AuthorizeAsync([], Ct));
    }

    [Fact]
    public async Task AuthorizeAsync_refuses_a_null_system_permission_list()
    {
        var service = Service(ClaimsPrincipalBuilder.Anonymous());

        Assert.False(await service.AuthorizeAsync(null, Ct));
    }

    [Fact]
    public async Task AuthorizeAsync_on_an_event_refuses_an_empty_event_permission_list_to_a_caller_holding_ManageEvent_on_it()
    {
        var evt = await SeedEvent();
        var service = Service(new ClaimsPrincipalBuilder().WithEvent(evt.Id, EventPermission.ManageEvent).Build());

        Assert.False(await service.AuthorizeAsync<Event>(evt.Id, [], Array.Empty<EventPermission>(), Ct));
    }

    [Fact]
    public async Task AuthorizeAsync_on_an_event_refuses_a_null_event_permission_list_to_a_caller_holding_ManageEvent_on_it()
    {
        var evt = await SeedEvent();
        var service = Service(new ClaimsPrincipalBuilder().WithEvent(evt.Id, EventPermission.ManageEvent).Build());

        Assert.False(await service.AuthorizeAsync<Event>(evt.Id, [], (EventPermission[])null, Ct));
    }

    [Fact]
    public async Task AuthorizeAsync_on_a_template_refuses_an_empty_template_permission_list_to_a_caller_holding_ManageEventTemplate_on_it()
    {
        var template = TestData.EventTemplate();
        await Seed(template);
        var service = Service(new ClaimsPrincipalBuilder().WithEventTemplate(template.Id, EventTemplatePermission.ManageEventTemplate).Build());

        Assert.False(await service.AuthorizeAsync<EventTemplate>(template.Id, [], Array.Empty<EventTemplatePermission>(), Ct));
    }

    [Fact]
    public async Task AuthorizeAsync_on_a_template_refuses_a_null_template_permission_list_to_a_caller_holding_ManageEventTemplate_on_it()
    {
        var template = TestData.EventTemplate();
        await Seed(template);
        var service = Service(new ClaimsPrincipalBuilder().WithEventTemplate(template.Id, EventTemplatePermission.ManageEventTemplate).Build());

        Assert.False(await service.AuthorizeAsync<EventTemplate>(template.Id, [], (EventTemplatePermission[])null, Ct));
    }

    [Fact]
    public async Task AuthorizeAsync_on_a_group_refuses_an_empty_group_permission_list_to_a_caller_holding_ManageMembership_on_it()
    {
        var group = TestData.Group();
        await Seed(group);
        var service = Service(new ClaimsPrincipalBuilder().WithGroup(group.Id, GroupPermission.ManageMembership).Build());

        Assert.False(await service.AuthorizeAsync<Group>(group.Id, [], Array.Empty<GroupPermission>(), Ct));
    }

    /// <summary>A null scoped list asks no scoped tier at all, so only the empty system list decides.</summary>
    [Fact]
    public async Task AuthorizeAsync_on_a_group_refuses_a_null_group_permission_list_to_a_caller_holding_ManageMembership_on_it()
    {
        var group = TestData.Group();
        await Seed(group);
        var service = Service(new ClaimsPrincipalBuilder().WithGroup(group.Id, GroupPermission.ManageMembership).Build());

        Assert.False(await service.AuthorizeAsync<Group>(group.Id, [], (GroupPermission[])null, Ct));
    }

    /// <summary>
    /// The system tier refuses a caller without <c>ViewEvents</c>, and the event tier then decides: the
    /// caller's <c>ViewEvent</c> admits its own event only.
    /// </summary>
    [Fact]
    public async Task AuthorizeAsync_on_an_event_refuses_ViewEvent_held_on_another_event_but_admits_it_on_its_own()
    {
        var own = await SeedEvent();
        var other = await SeedEvent();
        var service = Service(new ClaimsPrincipalBuilder().WithEvent(own.Id, EventPermission.ViewEvent).Build());

        Assert.False(await service.AuthorizeAsync<Event>(other.Id, [SystemPermission.ViewEvents], [EventPermission.ViewEvent], Ct));
        Assert.True(await service.AuthorizeAsync<Event>(own.Id, [SystemPermission.ViewEvents], [EventPermission.ViewEvent], Ct));
    }

    [Fact]
    public async Task AuthorizeAsync_on_an_event_admits_a_caller_holding_ManageEvents_without_a_claim_for_the_event()
    {
        var evt = await SeedEvent();
        var service = Service(new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ManageEvents).Build());

        Assert.True(await service.AuthorizeAsync<Event>(evt.Id, [SystemPermission.ManageEvents], [EventPermission.ManageEvent], Ct));
    }

    /// <summary>No controller authorizes on a template membership today; the service resolves one to its own template.</summary>
    [Fact]
    public async Task AuthorizeAsync_on_a_template_membership_admits_a_caller_holding_ManageEventTemplate_on_its_template()
    {
        var (template, membership) = await SeedTemplateMembership();
        var service = Service(new ClaimsPrincipalBuilder().WithEventTemplate(template.Id, EventTemplatePermission.ManageEventTemplate).Build());

        Assert.True(await service.AuthorizeAsync<EventTemplateMembership>(membership.Id, [], [EventTemplatePermission.ManageEventTemplate], Ct));
    }

    [Fact]
    public async Task AuthorizeAsync_on_a_template_membership_refuses_a_caller_holding_ManageEventTemplate_only_on_another_template()
    {
        var (_, membership) = await SeedTemplateMembership();
        var other = TestData.EventTemplate();
        await Seed(other);
        var service = Service(new ClaimsPrincipalBuilder().WithEventTemplate(other.Id, EventTemplatePermission.ManageEventTemplate).Build());

        Assert.False(await service.AuthorizeAsync<EventTemplateMembership>(membership.Id, [], [EventTemplatePermission.ManageEventTemplate], Ct));
    }

    [Fact]
    public async Task AuthorizeAsync_on_an_unknown_template_membership_refuses_a_caller_holding_ManageEventTemplate_on_a_template()
    {
        var (template, _) = await SeedTemplateMembership();
        var service = Service(new ClaimsPrincipalBuilder().WithEventTemplate(template.Id, EventTemplatePermission.ManageEventTemplate).Build());

        Assert.False(await service.AuthorizeAsync<EventTemplateMembership>(Guid.NewGuid(), [], [EventTemplatePermission.ManageEventTemplate], Ct));
    }

    private IAlloyAuthorizationService Service(ClaimsPrincipal user) =>
        AuthorizationHarness.CreateAlloyAuthorizationService(user, Db);

    private async Task<EventEntity> SeedEvent()
    {
        var template = TestData.EventTemplate();
        var evt = TestData.Event(template.Id);
        await Seed(template, evt);

        return evt;
    }

    private async Task<(EventTemplateEntity Template, EventTemplateMembershipEntity Membership)> SeedTemplateMembership()
    {
        var template = TestData.EventTemplate();
        var user = TestData.User();
        var membership = TestData.EventTemplateMembership(template.Id, TestData.EventTemplateRoles.Member, user.Id);
        await Seed(template, user, membership);

        return (template, membership);
    }
}
