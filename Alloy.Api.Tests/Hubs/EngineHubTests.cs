// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Hubs;
using Alloy.Api.Tests.Support;

namespace Alloy.Api.Tests.Hubs;

/// <summary>
/// <c>EngineHub</c> through <see cref="HubHarness"/>: which groups a connection joins. A hub method has no
/// request, so the caller is a principal from <see cref="ClaimsPrincipalBuilder"/> standing for the claims
/// transformer's output, read by Alloy's real authorization service; the memberships come from the
/// test's database.
/// </summary>
public class EngineHubTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task JoinEvent_adds_a_member_of_the_event_to_its_group()
    {
        var evt = TestData.Event(null);
        var user = TestData.User();
        await Seed(evt, user, TestData.EventMembership(evt.Id, TestData.EventRoles.Observer, user.Id));
        var harness = new HubHarness(user.Id, new ClaimsPrincipalBuilder().WithUserId(user.Id).Build());

        await Hub(harness).JoinEvent(evt.Id);

        await harness.Groups.Received(1).AddToGroupAsync(HubHarness.ConnectionId, evt.Id.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinEvent_leaves_out_a_member_of_another_event()
    {
        var evt = TestData.Event(null);
        var other = TestData.Event(null);
        var user = TestData.User();
        await Seed(evt, other, user, TestData.EventMembership(other.Id, TestData.EventRoles.Manager, user.Id));
        var harness = new HubHarness(user.Id, new ClaimsPrincipalBuilder().WithUserId(user.Id).WithEvent(other.Id, EventPermission.ViewEvent).Build());

        await Hub(harness).JoinEvent(evt.Id);

        await harness.Groups.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinEvent_adds_a_caller_holding_ViewEvents_to_any_events_group()
    {
        var evt = TestData.Event(null);
        await Seed(evt);
        var harness = Harness(b => b.WithSystemPermissions(SystemPermission.ViewEvents));

        await Hub(harness).JoinEvent(evt.Id);

        await harness.Groups.Received(1).AddToGroupAsync(HubHarness.ConnectionId, evt.Id.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinEvent_leaves_out_a_caller_holding_only_ViewEventTemplates_who_is_not_a_member()
    {
        var evt = TestData.Event(null);
        await Seed(evt);
        var harness = Harness(b => b.WithSystemPermissions(SystemPermission.ViewEventTemplates));

        await Hub(harness).JoinEvent(evt.Id);

        await harness.Groups.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinAdmin_adds_a_caller_holding_ViewEvents_to_the_admin_event_group()
    {
        var harness = Harness(b => b.WithSystemPermissions(SystemPermission.ViewEvents));

        await Hub(harness).JoinAdmin();

        Assert.Contains(EngineHub.ADMIN_EVENT_GROUP, harness.JoinedGroups);
    }

    [Fact]
    public async Task JoinAdmin_leaves_out_of_the_admin_event_group_a_caller_holding_only_ViewEventTemplates()
    {
        var harness = Harness(b => b.WithSystemPermissions(SystemPermission.ViewEventTemplates));

        await Hub(harness).JoinAdmin();

        Assert.DoesNotContain(EngineHub.ADMIN_EVENT_GROUP, harness.JoinedGroups);
    }

    [Fact]
    public async Task JoinAdmin_adds_a_caller_holding_ViewEventTemplates_to_the_admin_event_template_group()
    {
        var harness = Harness(b => b.WithSystemPermissions(SystemPermission.ViewEventTemplates));

        await Hub(harness).JoinAdmin();

        Assert.Contains(EngineHub.ADMIN_EVENT_TEMPLATE_GROUP, harness.JoinedGroups);
    }

    [Fact]
    public async Task JoinAdmin_leaves_out_of_the_admin_event_template_group_a_caller_holding_only_ViewEvents()
    {
        var harness = Harness(b => b.WithSystemPermissions(SystemPermission.ViewEvents));

        await Hub(harness).JoinAdmin();

        Assert.DoesNotContain(EngineHub.ADMIN_EVENT_TEMPLATE_GROUP, harness.JoinedGroups);
    }

    [Fact]
    public async Task JoinAdmin_adds_a_caller_holding_ViewGroups_to_the_admin_group_group()
    {
        var harness = Harness(b => b.WithSystemPermissions(SystemPermission.ViewGroups));

        await Hub(harness).JoinAdmin();

        Assert.Contains(EngineHub.ADMIN_GROUP_GROUP, harness.JoinedGroups);
    }

    [Fact]
    public async Task JoinAdmin_leaves_out_of_the_admin_group_group_a_caller_holding_only_ViewRoles()
    {
        var harness = Harness(b => b.WithSystemPermissions(SystemPermission.ViewRoles));

        await Hub(harness).JoinAdmin();

        Assert.DoesNotContain(EngineHub.ADMIN_GROUP_GROUP, harness.JoinedGroups);
    }

    [Fact]
    public async Task JoinAdmin_adds_a_caller_holding_ViewRoles_to_the_admin_role_group()
    {
        var harness = Harness(b => b.WithSystemPermissions(SystemPermission.ViewRoles));

        await Hub(harness).JoinAdmin();

        Assert.Contains(EngineHub.ADMIN_ROLE_GROUP, harness.JoinedGroups);
    }

    [Fact]
    public async Task JoinAdmin_leaves_out_of_the_admin_role_group_a_caller_holding_only_ViewGroups()
    {
        var harness = Harness(b => b.WithSystemPermissions(SystemPermission.ViewGroups));

        await Hub(harness).JoinAdmin();

        Assert.DoesNotContain(EngineHub.ADMIN_ROLE_GROUP, harness.JoinedGroups);
    }

    [Fact]
    public async Task JoinAdmin_adds_a_caller_holding_ViewUsers_to_the_admin_user_group()
    {
        var harness = Harness(b => b.WithSystemPermissions(SystemPermission.ViewUsers));

        await Hub(harness).JoinAdmin();

        Assert.Contains(EngineHub.ADMIN_USER_GROUP, harness.JoinedGroups);
    }

    [Fact]
    public async Task JoinAdmin_leaves_out_of_the_admin_user_group_a_caller_holding_only_ViewRoles()
    {
        var harness = Harness(b => b.WithSystemPermissions(SystemPermission.ViewRoles));

        await Hub(harness).JoinAdmin();

        Assert.DoesNotContain(EngineHub.ADMIN_USER_GROUP, harness.JoinedGroups);
    }

    [Fact]
    public async Task JoinAdmin_adds_a_caller_without_view_permissions_to_their_events_templates_and_managed_groups()
    {
        var user = TestData.User();
        var evt = TestData.Event(null);
        var template = TestData.EventTemplate();
        var group = TestData.Group();
        await Seed(user, evt, template, group,
            TestData.EventMembership(evt.Id, TestData.EventRoles.Observer, user.Id),
            TestData.EventTemplateMembership(template.Id, TestData.EventTemplateRoles.Observer, user.Id));
        var harness = new HubHarness(user.Id, new ClaimsPrincipalBuilder().WithUserId(user.Id)
            .WithGroup(group.Id, GroupPermission.ManageMembership).Build());

        await Hub(harness).JoinAdmin();

        await harness.Groups.Received(1).AddToGroupAsync(HubHarness.ConnectionId, evt.Id.ToString(), Arg.Any<CancellationToken>());
        await harness.Groups.Received(1).AddToGroupAsync(HubHarness.ConnectionId, template.Id.ToString(), Arg.Any<CancellationToken>());
        await harness.Groups.Received(1).AddToGroupAsync(HubHarness.ConnectionId, group.Id.ToString(), Arg.Any<CancellationToken>());
        await harness.Groups.DidNotReceive().AddToGroupAsync(HubHarness.ConnectionId, EngineHub.ADMIN_EVENT_GROUP, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeaveAdmin_removes_a_caller_holding_ViewEvents_from_the_admin_event_group()
    {
        var harness = Harness(b => b.WithSystemPermissions(SystemPermission.ViewEvents));

        await Hub(harness).LeaveAdmin();

        Assert.Contains(EngineHub.ADMIN_EVENT_GROUP, harness.LeftGroups);
    }

    [Fact]
    public async Task LeaveAdmin_leaves_out_the_admin_event_group_and_removes_the_events_for_a_member_holding_only_ViewEventTemplates()
    {
        var user = TestData.User();
        var evt = TestData.Event(null);
        await Seed(user, evt, TestData.EventMembership(evt.Id, TestData.EventRoles.Observer, user.Id));
        var harness = new HubHarness(user.Id, new ClaimsPrincipalBuilder().WithUserId(user.Id)
            .WithSystemPermissions(SystemPermission.ViewEventTemplates).Build());

        await Hub(harness).LeaveAdmin();

        Assert.DoesNotContain(EngineHub.ADMIN_EVENT_GROUP, harness.LeftGroups);
        Assert.Contains(evt.Id.ToString(), harness.LeftGroups);
    }

    [Fact]
    public async Task LeaveAdmin_removes_a_caller_holding_ViewEventTemplates_from_the_admin_event_template_group()
    {
        var harness = Harness(b => b.WithSystemPermissions(SystemPermission.ViewEventTemplates));

        await Hub(harness).LeaveAdmin();

        Assert.Contains(EngineHub.ADMIN_EVENT_TEMPLATE_GROUP, harness.LeftGroups);
    }

    [Fact]
    public async Task LeaveAdmin_leaves_out_the_admin_event_template_group_and_removes_the_templates_for_a_member_holding_only_ViewEvents()
    {
        var user = TestData.User();
        var template = TestData.EventTemplate();
        await Seed(user, template, TestData.EventTemplateMembership(template.Id, TestData.EventTemplateRoles.Observer, user.Id));
        var harness = new HubHarness(user.Id, new ClaimsPrincipalBuilder().WithUserId(user.Id)
            .WithSystemPermissions(SystemPermission.ViewEvents).Build());

        await Hub(harness).LeaveAdmin();

        Assert.DoesNotContain(EngineHub.ADMIN_EVENT_TEMPLATE_GROUP, harness.LeftGroups);
        Assert.Contains(template.Id.ToString(), harness.LeftGroups);
    }

    [Fact]
    public async Task LeaveAdmin_keeps_a_caller_holding_ViewGroups_in_the_groups_it_manages()
    {
        var group = TestData.Group();
        await Seed(group);
        var harness = Harness(b => b.WithSystemPermissions(SystemPermission.ViewGroups).WithGroup(group.Id, GroupPermission.ManageMembership));

        await Hub(harness).LeaveAdmin();

        Assert.DoesNotContain(group.Id.ToString(), harness.LeftGroups);
    }

    [Fact]
    public async Task LeaveAdmin_does_not_let_a_caller_holding_only_ViewRoles_stay_in_the_groups_it_manages()
    {
        var group = TestData.Group();
        await Seed(group);
        var harness = Harness(b => b.WithSystemPermissions(SystemPermission.ViewRoles).WithGroup(group.Id, GroupPermission.ManageMembership));

        await Hub(harness).LeaveAdmin();

        Assert.Contains(group.Id.ToString(), harness.LeftGroups);
    }

    [Fact]
    public async Task LeaveAdmin_removes_the_connection_from_the_role_and_user_groups_whatever_it_holds()
    {
        var harness = Harness(_ => { });

        await Hub(harness).LeaveAdmin();

        await harness.Groups.Received(1).RemoveFromGroupAsync(HubHarness.ConnectionId, EngineHub.ADMIN_ROLE_GROUP, Arg.Any<CancellationToken>());
        await harness.Groups.Received(1).RemoveFromGroupAsync(HubHarness.ConnectionId, EngineHub.ADMIN_USER_GROUP, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeaveEvent_removes_the_connection_from_the_events_group()
    {
        var eventId = Guid.NewGuid();
        var harness = Harness(_ => { });

        await Hub(harness).LeaveEvent(eventId);

        await harness.Groups.Received(1).RemoveFromGroupAsync(HubHarness.ConnectionId, eventId.ToString(), Arg.Any<CancellationToken>());
    }

    private static HubHarness Harness(Action<ClaimsPrincipalBuilder> configure)
    {
        var builder = new ClaimsPrincipalBuilder();
        configure(builder);

        return new HubHarness(builder.UserId, builder.Build());
    }

    private EngineHub Hub(HubHarness harness) =>
        harness.Attach(new EngineHub(Db, AuthorizationHarness.CreateAlloyAuthorizationService(harness.User, Db), harness.User));
}
