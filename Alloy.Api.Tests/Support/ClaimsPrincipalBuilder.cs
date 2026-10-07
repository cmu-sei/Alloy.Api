// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// The claim shapes are UserClaimsService.GetPermissionClaims': one "Permission" claim per system
// permission, and one JSON claim per event, event template and managed group.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Authorization;

namespace Alloy.Api.Tests.Support;

/// <summary>
/// Builds the principal the authorization stack sees for a signed-in user, for tests of that stack and
/// for service tests on <see cref="ServiceTestBase"/>, whose container runs no claims transformer. Never
/// for an HTTP test's caller, which is a <see cref="TestActor"/>.
/// </summary>
public sealed class ClaimsPrincipalBuilder
{
    private readonly List<Claim> _claims = [];
    private Guid _userId = Guid.NewGuid();
    private string _name = "Test User";

    public Guid UserId => _userId;

    public ClaimsPrincipalBuilder WithUserId(Guid userId)
    {
        _userId = userId;
        return this;
    }

    public ClaimsPrincipalBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>Adds system permissions, which grant across every resource.</summary>
    public ClaimsPrincipalBuilder WithSystemPermissions(params SystemPermission[] permissions)
    {
        foreach (var permission in permissions)
        {
            _claims.Add(new Claim(AuthorizationConstants.PermissionClaimType, permission.ToString()));
        }

        return this;
    }

    /// <summary>A raw system-permission value, for values that are not enum names.</summary>
    public ClaimsPrincipalBuilder WithRawSystemPermission(string value)
    {
        _claims.Add(new Claim(AuthorizationConstants.PermissionClaimType, value));
        return this;
    }

    /// <summary>The claim a membership on <paramref name="eventId"/> produces.</summary>
    public ClaimsPrincipalBuilder WithEvent(Guid eventId, params EventPermission[] permissions)
    {
        _claims.Add(new Claim(
            AuthorizationConstants.EventPermissionClaimType,
            new EventPermissionClaim { EventId = eventId, Permissions = permissions }.ToString()));
        return this;
    }

    /// <summary>The claim a membership on <paramref name="eventTemplateId"/> (or its publication) produces.</summary>
    public ClaimsPrincipalBuilder WithEventTemplate(Guid eventTemplateId, params EventTemplatePermission[] permissions)
    {
        _claims.Add(new Claim(
            AuthorizationConstants.EventTemplatePermissionClaimType,
            new EventTemplatePermissionClaim { EventTemplateId = eventTemplateId, Permissions = permissions }.ToString()));
        return this;
    }

    /// <summary>The claim managing <paramref name="groupId"/> produces.</summary>
    public ClaimsPrincipalBuilder WithGroup(Guid groupId, params GroupPermission[] permissions)
    {
        _claims.Add(new Claim(
            AuthorizationConstants.GroupPermissionsClaimType,
            new GroupPermissionsClaim { GroupId = groupId, Permissions = permissions }.ToString()));
        return this;
    }

    /// <summary>An arbitrary claim, for asserting that unrelated claim types are ignored.</summary>
    public ClaimsPrincipalBuilder WithClaim(string type, string value)
    {
        _claims.Add(new Claim(type, value));
        return this;
    }

    public ClaimsPrincipal Build()
    {
        var claims = new List<Claim>(_claims) { new("sub", _userId.ToString()), new("name", _name) };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    /// <summary>An authenticated principal with no permissions, the baseline every check must reject.</summary>
    public static ClaimsPrincipal Anonymous() => new ClaimsPrincipalBuilder().Build();
}
