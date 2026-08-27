// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.ViewModels;
using Alloy.Api.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Alloy.Api.Infrastructure.Authorization;

public interface IAlloyAuthorizationService
{
    Task<bool> AuthorizeAsync(
        SystemPermission[] requiredSystemPermissions,
        CancellationToken cancellationToken);

    Task<bool> AuthorizeAsync<T>(
        Guid? resourceId,
        SystemPermission[] requiredSystemPermissions,
        EventPermission[] requiredEventPermissions,
        CancellationToken cancellationToken) where T : IAuthorizationType;

    Task<bool> AuthorizeAsync<T>(
        Guid? resourceId,
        SystemPermission[] requiredSystemPermissions,
        EventTemplatePermission[] requiredEventTemplatePermissions,
        CancellationToken cancellationToken) where T : IAuthorizationType;

    Task<bool> AuthorizeAsync<T>(
        Guid? resourceId,
        SystemPermission[] requiredSystemPermissions,
        GroupPermission[] requiredGroupPermissions,
        CancellationToken cancellationToken) where T : IAuthorizationType;

    IEnumerable<Guid> GetAuthorizedEventIds();
    IEnumerable<SystemPermission> GetSystemPermissions();
    IEnumerable<EventPermissionClaim> GetEventPermissions(Guid? eventId = null);
    IEnumerable<EventTemplatePermissionClaim> GetEventTemplatePermissions(Guid? eventTemplateId = null);
    IEnumerable<GroupPermissionsClaim> GetGroupPermissions(Guid? groupId = null);
}

public class AuthorizationService(
    IAuthorizationService authService,
    IIdentityResolver identityResolver,
    AlloyContext dbContext) : IAlloyAuthorizationService
{
    public async Task<bool> AuthorizeAsync(
        SystemPermission[] requiredSystemPermissions,
        CancellationToken cancellationToken)
    {
        return await Authorize<IAuthorizationType>(
            null,
            requiredSystemPermissions,
            null,
            null,
            null,
            cancellationToken);
    }

    public async Task<bool> AuthorizeAsync<T>(
        Guid? resourceId,
        SystemPermission[] requiredSystemPermissions,
        EventPermission[] requiredEventPermissions,
        CancellationToken cancellationToken) where T : IAuthorizationType
    {
        return await Authorize<T>(
            resourceId,
            requiredSystemPermissions,
            requiredEventPermissions,
            null,
            null,
            cancellationToken);
    }

    public async Task<bool> AuthorizeAsync<T>(
        Guid? resourceId,
        SystemPermission[] requiredSystemPermissions,
        EventTemplatePermission[] requiredEventTemplatePermissions,
        CancellationToken cancellationToken) where T : IAuthorizationType
    {
        return await Authorize<T>(
            resourceId,
            requiredSystemPermissions,
            null,
            requiredEventTemplatePermissions,
            null,
            cancellationToken);
    }

    public async Task<bool> AuthorizeAsync<T>(
        Guid? resourceId,
        SystemPermission[] requiredSystemPermissions,
        GroupPermission[] requiredGroupPermissions,
        CancellationToken cancellationToken) where T : IAuthorizationType
    {
        return await Authorize<T>(
            resourceId,
            requiredSystemPermissions,
            null,
            null,
            requiredGroupPermissions,
            cancellationToken);
    }

    public IEnumerable<Guid> GetAuthorizedEventIds()
    {
        return identityResolver.GetClaimsPrincipal().Claims
            .Where(x => x.Type == AuthorizationConstants.EventPermissionClaimType)
            .Select(x => EventPermissionClaim.FromString(x.Value).EventId)
            .ToList();
    }

    public IEnumerable<SystemPermission> GetSystemPermissions()
    {
        var principal = identityResolver.GetClaimsPrincipal();
        var claims = principal.Claims;
        var permissions = claims
           .Where(x => x.Type == AuthorizationConstants.PermissionClaimType)
           .Select(x =>
           {
               if (Enum.TryParse<SystemPermission>(x.Value, out var permission))
                   return permission;

               return (SystemPermission?)null;
           })
           .Where(x => x.HasValue)
           .Select(x => x.Value)
           .ToList();
        return permissions;
    }

    public IEnumerable<EventPermissionClaim> GetEventPermissions(Guid? eventId = null)
    {
        var permissions = identityResolver.GetClaimsPrincipal().Claims
           .Where(x => x.Type == AuthorizationConstants.EventPermissionClaimType)
           .Select(x => EventPermissionClaim.FromString(x.Value));

        if (eventId.HasValue)
        {
            permissions = permissions.Where(x => x.EventId == eventId.Value);
        }

        return permissions;
    }

    public IEnumerable<EventTemplatePermissionClaim> GetEventTemplatePermissions(Guid? eventTemplateId = null)
    {
        var permissions = identityResolver.GetClaimsPrincipal().Claims
           .Where(x => x.Type == AuthorizationConstants.EventTemplatePermissionClaimType)
           .Select(x => EventTemplatePermissionClaim.FromString(x.Value));

        if (eventTemplateId.HasValue)
        {
            permissions = permissions.Where(x => x.EventTemplateId == eventTemplateId.Value);
        }

        return permissions;
    }

    public IEnumerable<GroupPermissionsClaim> GetGroupPermissions(Guid? groupId = null)
    {
        var permissions = identityResolver.GetClaimsPrincipal().Claims
           .Where(x => x.Type == AuthorizationConstants.GroupPermissionsClaimType)
           .Select(x => GroupPermissionsClaim.FromString(x.Value));

        if (groupId.HasValue)
        {
            permissions = permissions.Where(x => x.GroupId == groupId.Value);
        }

        return permissions;
    }

    private async Task<bool> Authorize<T>(
        Guid? resourceId,
        SystemPermission[] requiredSystemPermissions,
        EventPermission[] requiredEventPermissions,
        EventTemplatePermission[] requiredEventTemplatePermissions,
        GroupPermission[] requiredGroupPermissions,
        CancellationToken cancellationToken) where T : IAuthorizationType
    {
        ValidateScopedPermissionTypes(
            requiredEventPermissions,
            requiredEventTemplatePermissions,
            requiredGroupPermissions);

        var claimsPrincipal = identityResolver.GetClaimsPrincipal();
        var permissionRequirement = new SystemPermissionRequirement(requiredSystemPermissions);
        var permissionResult = await authService.AuthorizeAsync(claimsPrincipal, null, permissionRequirement);

        if (permissionResult.Succeeded)
            return true;

        if (requiredEventPermissions != null)
            return await AuthorizeEvent<T>(
                claimsPrincipal,
                resourceId,
                requiredEventPermissions,
                cancellationToken);

        if (requiredEventTemplatePermissions != null)
            return await AuthorizeEventTemplate<T>(
                claimsPrincipal,
                resourceId,
                requiredEventTemplatePermissions,
                cancellationToken);

        if (requiredGroupPermissions != null)
            return await AuthorizeGroup<T>(
                claimsPrincipal,
                resourceId,
                requiredGroupPermissions,
                cancellationToken);

        return false;
    }

    private static void ValidateScopedPermissionTypes(
        EventPermission[] eventPermissions,
        EventTemplatePermission[] eventTemplatePermissions,
        GroupPermission[] groupPermissions)
    {
        var scopedPermissionTypeCount =
            (eventPermissions != null ? 1 : 0) +
            (eventTemplatePermissions != null ? 1 : 0) +
            (groupPermissions != null ? 1 : 0);

        if (scopedPermissionTypeCount > 1)
            throw new InvalidOperationException(
                "Only one scoped permission type can be provided for authorization.");
    }

    private async Task<bool> AuthorizeEvent<T>(
        ClaimsPrincipal claimsPrincipal,
        Guid? resourceId,
        EventPermission[] requiredEventPermissions,
        CancellationToken cancellationToken) where T : IAuthorizationType
    {
        var eventId = resourceId.HasValue
            ? await GetEventId<T>(resourceId.Value, cancellationToken)
            : null;

        var eventPermissionRequirement = new EventPermissionRequirement(requiredEventPermissions, eventId);
        var eventPermissionResult = await authService.AuthorizeAsync(claimsPrincipal, null, eventPermissionRequirement);

        return eventPermissionResult.Succeeded;
    }

    private async Task<bool> AuthorizeEventTemplate<T>(
        ClaimsPrincipal claimsPrincipal,
        Guid? resourceId,
        EventTemplatePermission[] requiredEventTemplatePermissions,
        CancellationToken cancellationToken) where T : IAuthorizationType
    {
        var eventTemplateId = resourceId.HasValue
            ? await GetEventTemplateId<T>(resourceId.Value, cancellationToken)
            : null;

        var eventTemplatePermissionRequirement = new EventTemplatePermissionRequirement(requiredEventTemplatePermissions, eventTemplateId);
        var eventTemplatePermissionResult = await authService.AuthorizeAsync(claimsPrincipal, null, eventTemplatePermissionRequirement);

        return eventTemplatePermissionResult.Succeeded;
    }

    private async Task<bool> AuthorizeGroup<T>(
        ClaimsPrincipal claimsPrincipal,
        Guid? resourceId,
        GroupPermission[] requiredGroupPermissions,
        CancellationToken cancellationToken) where T : IAuthorizationType
    {
        var groupId = resourceId.HasValue
            ? await GetGroupId<T>(resourceId.Value, cancellationToken)
            : null;

        var groupPermissionRequirement = new GroupPermissionRequirement(requiredGroupPermissions, groupId);
        var groupPermissionResult = await authService.AuthorizeAsync(claimsPrincipal, null, groupPermissionRequirement);

        return groupPermissionResult.Succeeded;
    }

    private async Task<Guid?> GetEventId<T>(Guid resourceId, CancellationToken cancellationToken)
    {
        return typeof(T) switch
        {
            var t when t == typeof(Event) => resourceId,
            var t when t == typeof(EventMembership) => await GetEventIdFromEventMembership(resourceId, cancellationToken),
            _ => throw new NotImplementedException($"Handler for type {typeof(T).Name} is not implemented.")
        };
    }

    private async Task<Guid?> GetEventTemplateId<T>(Guid resourceId, CancellationToken cancellationToken)
    {
        return typeof(T) switch
        {
            var t when t == typeof(EventTemplate) => resourceId,
            var t when t == typeof(Event) => await GetEventTemplateIdFromEvent(resourceId, cancellationToken),
            var t when t == typeof(EventMembership) => await GetEventTemplateIdFromEventTemplateMembership(resourceId, cancellationToken),
            _ => throw new NotImplementedException($"Handler for type {typeof(T).Name} is not implemented.")
        };
    }

    private async Task<Guid?> GetGroupId<T>(Guid resourceId, CancellationToken cancellationToken)
    {
        return typeof(T) switch
        {
            var t when t == typeof(Group) => resourceId,
            var t when t == typeof(GroupMembership) => await GetGroupIdFromGroupMembership(resourceId, cancellationToken),
            _ => throw new NotImplementedException($"Group handler for type {typeof(T).Name} is not implemented.")
        };
    }

    private async Task<Guid?> GetEventIdFromEventMembership(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.EventMemberships
            .Where(x => x.Id == id)
            .Select(x => (Guid?)x.EventId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid?> GetGroupIdFromGroupMembership(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.GroupMemberships
            .Where(x => x.Id == id)
            .Select(x => (Guid?)x.GroupId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid> GetEventIdFromPlayerView(Guid id, CancellationToken cancellationToken)
    {
        return (Guid)await dbContext.Events
            .Where(x => x.ViewId == id)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid?> GetEventTemplateIdFromEvent(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.Events
            .Where(x => x.Id == id)
            .Select(x => x.EventTemplateId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid?> GetEventTemplateIdFromEventTemplateMembership(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.EventTemplateMemberships
            .Where(x => x.Id == id)
            .Select(x => (Guid?)x.EventTemplateId)
            .FirstOrDefaultAsync(cancellationToken);
    }

}
