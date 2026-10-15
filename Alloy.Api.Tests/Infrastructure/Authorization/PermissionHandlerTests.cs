// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Authorization;
using Alloy.Api.Tests.Support;

namespace Alloy.Api.Tests.Infrastructure.Authorization;

/// <summary>
/// The requirement handlers, run directly on principals in the claim shapes <c>UserClaimsService</c>
/// produces. Any one of the required permissions satisfies a requirement; a resource requirement needs a
/// claim for that very resource.
/// </summary>
public class PermissionHandlerTests
{
    [Fact]
    public async Task A_system_requirement_is_met_by_any_one_of_its_permissions()
    {
        var user = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.EditEvents).Build();

        var context = await AuthorizationHarness.HandleAsync(new SystemPermissionHandler(),
            new SystemPermissionRequirement([SystemPermission.ViewEvents, SystemPermission.EditEvents]), user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task A_system_requirement_is_not_met_by_an_unrelated_permission()
    {
        var user = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ViewGroups).Build();

        var context = await AuthorizationHarness.HandleAsync(new SystemPermissionHandler(),
            new SystemPermissionRequirement([SystemPermission.ViewEvents]), user);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task A_system_permission_value_that_is_not_an_enum_name_grants_nothing()
    {
        var user = new ClaimsPrincipalBuilder().WithRawSystemPermission("viewevents").Build();

        var context = await AuthorizationHarness.HandleAsync(new SystemPermissionHandler(),
            new SystemPermissionRequirement([SystemPermission.ViewEvents]), user);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task An_event_requirement_is_met_by_the_permission_on_that_event()
    {
        var eventId = Guid.NewGuid();
        var user = new ClaimsPrincipalBuilder().WithEvent(eventId, EventPermission.ViewEvent).Build();

        var context = await AuthorizationHarness.HandleAsync(new EventPermissionHandler(),
            new EventPermissionRequirement([EventPermission.ViewEvent], eventId), user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task An_event_requirement_fails_for_the_permission_on_another_event()
    {
        var user = new ClaimsPrincipalBuilder().WithEvent(Guid.NewGuid(), EventPermission.ManageEvent).Build();

        var context = await AuthorizationHarness.HandleAsync(new EventPermissionHandler(),
            new EventPermissionRequirement([EventPermission.ManageEvent], Guid.NewGuid()), user);

        Assert.True(context.HasFailed);
    }

    /// <summary>A route whose membership or event does not resolve passes no id, which fails rather than allowing.</summary>
    [Fact]
    public async Task An_event_requirement_without_an_event_fails()
    {
        var user = new ClaimsPrincipalBuilder().WithEvent(Guid.NewGuid(), EventPermission.ViewEvent).Build();

        var context = await AuthorizationHarness.HandleAsync(new EventPermissionHandler(),
            new EventPermissionRequirement([EventPermission.ViewEvent], null), user);

        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task An_event_template_requirement_is_not_met_by_another_permission_on_that_template()
    {
        var templateId = Guid.NewGuid();
        var user = new ClaimsPrincipalBuilder().WithEventTemplate(templateId, EventTemplatePermission.ViewEventTemplate).Build();

        var context = await AuthorizationHarness.HandleAsync(new EventTemplatePermissionHandler(),
            new EventTemplatePermissionRequirement([EventTemplatePermission.EditEventTemplate], templateId), user);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task A_group_requirement_is_met_by_ManageMembership_on_that_group()
    {
        var groupId = Guid.NewGuid();
        var user = new ClaimsPrincipalBuilder().WithGroup(groupId, GroupPermission.ManageMembership).Build();

        var context = await AuthorizationHarness.HandleAsync(new GroupPermissionsHandler(),
            new GroupPermissionRequirement([GroupPermission.ManageMembership], groupId), user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task The_framework_service_wired_as_production_wires_it_runs_the_handlers()
    {
        var user = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.ManageRoles).Build();

        var result = await AuthorizationHarness.CreateFrameworkAuthorizationService()
            .AuthorizeAsync(user, null, [new SystemPermissionRequirement([SystemPermission.ManageRoles])]);

        Assert.True(result.Succeeded);
    }
}
