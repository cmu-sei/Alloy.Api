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
    public async Task JoinAdmin_adds_a_caller_holding_every_view_permission_to_each_admin_group()
    {
        var harness = Harness(b => b.WithSystemPermissions(
            SystemPermission.ViewEvents, SystemPermission.ViewEventTemplates, SystemPermission.ViewGroups,
            SystemPermission.ViewRoles, SystemPermission.ViewUsers));

        await Hub(harness).JoinAdmin();

        await Joined(harness, EngineHub.ADMIN_EVENT_GROUP);
        await Joined(harness, EngineHub.ADMIN_EVENT_TEMPLATE_GROUP);
        await Joined(harness, EngineHub.ADMIN_GROUP_GROUP);
        await Joined(harness, EngineHub.ADMIN_ROLE_GROUP);
        await Joined(harness, EngineHub.ADMIN_USER_GROUP);
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

    private static Task Joined(HubHarness harness, string group) =>
        harness.Groups.Received(1).AddToGroupAsync(HubHarness.ConnectionId, group, Arg.Any<CancellationToken>());

    private static HubHarness Harness(Action<ClaimsPrincipalBuilder> configure)
    {
        var builder = new ClaimsPrincipalBuilder();
        configure(builder);

        return new HubHarness(builder.UserId, builder.Build());
    }

    private EngineHub Hub(HubHarness harness) =>
        harness.Attach(new EngineHub(Db, AuthorizationHarness.CreateAlloyAuthorizationService(harness.User, Db), harness.User));
}
