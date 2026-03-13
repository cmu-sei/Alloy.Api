// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Authorization;
using Alloy.Api.ViewModels;

namespace Alloy.Api.Tests.Integration.Fixtures;

/// <summary>
/// A permissive authorization service for integration tests that allows all operations.
/// </summary>
public class TestAlloyAuthorizationService : IAlloyAuthorizationService
{
    public Task<bool> AuthorizeAsync(
        SystemPermission[] requiredSystemPermissions,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }

    public Task<bool> AuthorizeAsync<T>(
        Guid? resourceId,
        SystemPermission[] requiredSystemPermissions,
        EventPermission[] requiredEventPermissions,
        CancellationToken cancellationToken) where T : IAuthorizationType
    {
        return Task.FromResult(true);
    }

    public Task<bool> AuthorizeAsync<T>(
        Guid? resourceId,
        SystemPermission[] requiredSystemPermissions,
        EventTemplatePermission[] requiredEventTemplatePermissions,
        CancellationToken cancellationToken) where T : IAuthorizationType
    {
        return Task.FromResult(true);
    }

    public IEnumerable<Guid> GetAuthorizedEventIds() => [];

    public IEnumerable<SystemPermission> GetSystemPermissions() =>
        Enum.GetValues<SystemPermission>();

    public IEnumerable<EventPermissionClaim> GetEventPermissions(Guid? eventId = null) => [];

    public IEnumerable<EventTemplatePermissionClaim> GetEventTemplatePermissions(Guid? eventTemplateId = null) => [];
}
