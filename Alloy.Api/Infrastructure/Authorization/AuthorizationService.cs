// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
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

    IEnumerable<Guid> GetAuthorizedEventIds();
    IEnumerable<SystemPermission> GetSystemPermissions();
    IEnumerable<EventPermissionClaim> GetEventPermissions(Guid? eventId = null);
    IEnumerable<EventTemplatePermissionClaim> GetEventTemplatePermissions(Guid? eventTemplateId = null);
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
        return await HasSystemPermission<IAuthorizationType>(requiredSystemPermissions);
    }

    public async Task<bool> AuthorizeAsync<T>(
        Guid? resourceId,
        SystemPermission[] requiredSystemPermissions,
        EventPermission[] requiredEventPermissions,
        CancellationToken cancellationToken) where T : IAuthorizationType
    {
        var claimsPrincipal = identityResolver.GetClaimsPrincipal();
        bool succeeded = await HasSystemPermission<IAuthorizationType>(requiredSystemPermissions);

        if (!succeeded && resourceId.HasValue)
        {
            var eventId = await GetEventId<T>(resourceId.Value, cancellationToken);

            if (eventId != null)
            {
                var eventPermissionRequirement = new EventPermissionRequirement(requiredEventPermissions, eventId.Value);
                var eventPermissionResult = await authService.AuthorizeAsync(claimsPrincipal, null, eventPermissionRequirement);

                succeeded = eventPermissionResult.Succeeded;
            }

        }

        return succeeded;
    }

    public async Task<bool> AuthorizeAsync<T>(
        Guid? resourceId,
        SystemPermission[] requiredSystemPermissions,
        EventTemplatePermission[] requiredEventTemplatePermissions,
        CancellationToken cancellationToken) where T : IAuthorizationType
    {
        var claimsPrincipal = identityResolver.GetClaimsPrincipal();
        bool succeeded = await HasSystemPermission<IAuthorizationType>(requiredSystemPermissions);

        if (!succeeded && resourceId.HasValue)
        {
            var eventTemplateId = await GetEventTemplateId<T>(resourceId.Value, cancellationToken);

            if (eventTemplateId != null)
            {
                var eventTemplatePermissionRequirement = new EventTemplatePermissionRequirement(requiredEventTemplatePermissions, eventTemplateId.Value);
                var eventTemplatePermissionResult = await authService.AuthorizeAsync(claimsPrincipal, null, eventTemplatePermissionRequirement);

                succeeded = eventTemplatePermissionResult.Succeeded;
            }

        }

        return succeeded;
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

    private async Task<bool> HasSystemPermission<T>(
        SystemPermission[] requiredSystemPermissions) where T : IAuthorizationType
    {
        var claimsPrincipal = identityResolver.GetClaimsPrincipal();
        var permissionRequirement = new SystemPermissionRequirement(requiredSystemPermissions);
        var permissionResult = await authService.AuthorizeAsync(claimsPrincipal, null, permissionRequirement);

        return permissionResult.Succeeded;
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

    private async Task<Guid> GetEventIdFromEventMembership(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.EventMemberships
            .Where(x => x.Id == id)
            .Select(x => x.EventId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid> GetEventIdFromPlayerView(Guid id, CancellationToken cancellationToken)
    {
        return (Guid)await dbContext.Events
            .Where(x => x.ViewId == id)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid> GetEventTemplateIdFromEvent(Guid id, CancellationToken cancellationToken)
    {
        return (Guid)await dbContext.Events
            .Where(x => x.Id == id)
            .Select(x => x.EventTemplateId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<Guid> GetEventTemplateIdFromEventTemplateMembership(Guid id, CancellationToken cancellationToken)
    {
        return await dbContext.EventTemplateMemberships
            .Where(x => x.Id == id)
            .Select(x => x.EventTemplateId)
            .FirstOrDefaultAsync(cancellationToken);
    }

}