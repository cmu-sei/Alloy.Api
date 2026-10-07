// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// Runs each actor shape through the real UserClaimsService, with caching and IdP roles and groups off as
// TestConfiguration has them.

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Authorization;
using Alloy.Api.Infrastructure.Options;
using Alloy.Api.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Alloy.Api.Tests.Support;

/// <summary>Tests for <see cref="TestActorBuilder"/>: an actor that holds more than asked turns an authorization test into a formality.</summary>
public class TestActorTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task WithAllSystemPermissions_grants_every_system_permission()
    {
        var actor = await Actor().WithAllSystemPermissions().SeedAsync();

        Assert.Equal(Enum.GetNames<SystemPermission>().Order(), SystemPermissions(await ClaimsOf(actor)));
    }

    [Fact]
    public async Task WithSystemPermissions_grants_exactly_what_it_names()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEvents).SeedAsync();

        Assert.Equal([nameof(SystemPermission.ViewEvents)], SystemPermissions(await ClaimsOf(actor)));
    }

    [Fact]
    public async Task An_actor_with_no_role_holds_nothing()
    {
        var actor = await Actor().SeedAsync();

        Assert.DoesNotContain((await ClaimsOf(actor)).Claims, x => x.Type != "sub");
    }

    [Fact]
    public void WithRole_after_WithSystemPermissions_throws()
    {
        var builder = Actor().WithSystemPermissions(SystemPermission.ViewEvents);

        Assert.Throws<InvalidOperationException>(() => builder.WithRole(TestData.Roles.Administrator));
    }

    [Fact]
    public void WithSystemPermissions_after_WithRole_throws()
    {
        var builder = Actor().WithRole(TestData.Roles.Observer);

        Assert.Throws<InvalidOperationException>(() => builder.WithSystemPermissions(SystemPermission.ViewEvents));
    }

    [Fact]
    public async Task OnEvent_with_permissions_grants_exactly_those_on_that_event()
    {
        var evt = TestData.Event(null);
        await Seed(evt);

        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ExecuteEvent]).SeedAsync();

        var claim = Assert.Single(EventClaims(await ClaimsOf(actor)));
        Assert.Equal(evt.Id, claim.EventId);
        Assert.Equal([EventPermission.ExecuteEvent], claim.Permissions);
    }

    [Fact]
    public async Task OnEvent_with_the_seeded_manager_role_grants_every_event_permission()
    {
        var evt = TestData.Event(null);
        await Seed(evt);

        var actor = await Actor().OnEvent(evt.Id, roleId: TestData.EventRoles.Manager).SeedAsync();

        Assert.Equal(Enum.GetValues<EventPermission>(), Assert.Single(EventClaims(await ClaimsOf(actor))).Permissions.Order());
    }

    [Fact]
    public async Task OnEvent_through_a_group_grants_the_group_membership_role()
    {
        var evt = TestData.Event(null);
        await Seed(evt);

        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ViewEvent], throughGroup: true).SeedAsync();

        var claim = Assert.Single(EventClaims(await ClaimsOf(actor)));
        Assert.Equal(evt.Id, claim.EventId);
        Assert.Equal([EventPermission.ViewEvent], claim.Permissions);
    }

    [Fact]
    public void OnEvent_with_neither_permissions_nor_a_role_throws()
    {
        Assert.Throws<InvalidOperationException>(() => Actor().OnEvent(Guid.NewGuid()));
    }

    [Fact]
    public async Task OnEventTemplate_with_permissions_grants_exactly_those_on_that_template()
    {
        var template = TestData.EventTemplate();
        await Seed(template);

        var actor = await Actor().OnEventTemplate(template.Id, [EventTemplatePermission.EditEventTemplate]).SeedAsync();

        var claim = Assert.Single(EventTemplateClaims(await ClaimsOf(actor)));
        Assert.Equal(template.Id, claim.EventTemplateId);
        Assert.Equal([EventTemplatePermission.EditEventTemplate], claim.Permissions);
    }

    [Fact]
    public async Task OnEventTemplate_through_a_group_grants_the_group_membership_role()
    {
        var template = TestData.EventTemplate();
        await Seed(template);

        var actor = await Actor()
            .OnEventTemplate(template.Id, [EventTemplatePermission.ManageEventTemplate], throughGroup: true)
            .SeedAsync();

        var claim = Assert.Single(EventTemplateClaims(await ClaimsOf(actor)));
        Assert.Equal(template.Id, claim.EventTemplateId);
        Assert.Equal([EventTemplatePermission.ManageEventTemplate], claim.Permissions);
    }

    [Fact]
    public async Task InGroup_as_a_manager_grants_ManageMembership_on_that_group()
    {
        var group = TestData.Group();
        await Seed(group);

        var actor = await Actor().InGroup(group.Id, GroupMembershipRole.Manager).SeedAsync();

        var claim = Assert.Single(GroupClaims(await ClaimsOf(actor)));
        Assert.Equal(group.Id, claim.GroupId);
        Assert.Equal([GroupPermission.ManageMembership], claim.Permissions);
    }

    [Fact]
    public async Task InGroup_as_a_member_grants_nothing_on_the_group()
    {
        var group = TestData.Group();
        await Seed(group);

        var actor = await Actor().InGroup(group.Id).SeedAsync();

        Assert.Empty(GroupClaims(await ClaimsOf(actor)));
    }

    [Fact]
    public async Task OnNewEvent_grants_exactly_what_it_names_on_the_minted_event()
    {
        var template = TestData.EventTemplate();
        await Seed(template);

        var actor = await Actor().OnNewEvent(template.Id, EventPermission.ViewEvent).SeedAsync();

        var claim = Assert.Single(EventClaims(await ClaimsOf(actor)));
        Assert.Equal(Assert.Single(actor.NewEvents), claim.EventId);
        Assert.Equal([EventPermission.ViewEvent], claim.Permissions);
    }

    /// <summary>The minted template is unpublished, so it adds no <c>ViewEventTemplate</c> of its own.</summary>
    [Fact]
    public async Task OnNewEventTemplate_grants_exactly_what_it_names_on_the_minted_template()
    {
        var actor = await Actor().OnNewEventTemplate(EventTemplatePermission.EditEventTemplate).SeedAsync();

        var claim = Assert.Single(EventTemplateClaims(await ClaimsOf(actor)));
        Assert.Equal(Assert.Single(actor.NewEventTemplates), claim.EventTemplateId);
        Assert.Equal([EventTemplatePermission.EditEventTemplate], claim.Permissions);
    }

    [Fact]
    public async Task OnNewGroup_as_a_manager_grants_ManageMembership_on_the_minted_group_only()
    {
        var actor = await Actor().OnNewGroup(GroupMembershipRole.Manager).SeedAsync();

        var claims = await ClaimsOf(actor);

        var claim = Assert.Single(GroupClaims(claims));
        Assert.Equal(Assert.Single(actor.NewGroups), claim.GroupId);
        Assert.Equal([GroupPermission.ManageMembership], claim.Permissions);
        Assert.Empty(EventClaims(claims));
    }

    private TestActorBuilder Actor() => new(Db, Ct);

    private async Task<ClaimsPrincipal> ClaimsOf(TestActor actor)
    {
        await using var context = NewContext();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new UserClaimsService(context, cache, new ClaimsTransformationOptions());
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", actor.Id.ToString())], "Test"));

        return await service.AddUserClaims(principal, update: false);
    }

    private static string[] SystemPermissions(ClaimsPrincipal principal) =>
        [.. principal.Claims
            .Where(x => x.Type == AuthorizationConstants.PermissionClaimType)
            .Select(x => x.Value)
            .Order()];

    private static EventPermissionClaim[] EventClaims(ClaimsPrincipal principal) =>
        [.. principal.Claims
            .Where(x => x.Type == AuthorizationConstants.EventPermissionClaimType)
            .Select(x => EventPermissionClaim.FromString(x.Value))];

    private static EventTemplatePermissionClaim[] EventTemplateClaims(ClaimsPrincipal principal) =>
        [.. principal.Claims
            .Where(x => x.Type == AuthorizationConstants.EventTemplatePermissionClaimType)
            .Select(x => EventTemplatePermissionClaim.FromString(x.Value))];

    private static GroupPermissionsClaim[] GroupClaims(ClaimsPrincipal principal) =>
        [.. principal.Claims
            .Where(x => x.Type == AuthorizationConstants.GroupPermissionsClaimType)
            .Select(x => GroupPermissionsClaim.FromString(x.Value))];
}
