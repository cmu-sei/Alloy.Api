// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Authorization;
using Alloy.Api.Infrastructure.Options;
using Alloy.Api.Services;
using Alloy.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Alloy.Api.Tests.Services;

/// <summary>
/// <see cref="UserClaimsService"/>, the claims transformer's source: the paths the HTTP tests turn off
/// (roles and groups named in the token, the cache) and the user row it writes for a new subject.
/// </summary>
public class UserClaimsServiceTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly ClaimsTransformationOptions FromIdP = new()
    {
        UseRolesFromIdP = true,
        RolesClaimPath = "realm_access.roles",
        UseGroupsFromIdP = true,
        GroupsClaimPath = "groups"
    };

    [Fact]
    public async Task A_system_role_named_in_the_token_grants_its_permissions_in_any_case()
    {
        var role = TestData.SystemRole("Range Tech", permissions: [SystemPermission.ViewEvents]);
        var user = TestData.User();
        await Seed(role, user);
        var token = Token(user.Id, new Claim("realm_access", """{"roles":["range tech"]}""", JsonClaimValueTypes.Json));

        var principal = await Claims(FromIdP, token);

        Assert.Equal([nameof(SystemPermission.ViewEvents)], SystemPermissions(principal));
    }

    [Fact]
    public async Task A_role_named_in_the_token_is_ignored_when_roles_from_the_IdP_are_off()
    {
        var user = TestData.User();
        await Seed(user);
        var token = Token(user.Id, new Claim("realm_access", """{"roles":["Administrator"]}""", JsonClaimValueTypes.Json));

        var principal = await Claims(new ClaimsTransformationOptions(), token);

        Assert.Empty(SystemPermissions(principal));
    }

    [Fact]
    public async Task A_group_named_in_the_token_grants_the_groups_event_memberships()
    {
        var group = TestData.Group("White Cell");
        var evt = TestData.Event(null);
        var user = TestData.User();
        await Seed(group, evt, user, TestData.EventMembership(evt.Id, TestData.EventRoles.Observer, groupId: group.Id));
        var token = Token(user.Id, new Claim("groups", "white cell"));

        var principal = await Claims(FromIdP, token);

        var claim = Assert.Single(principal.Claims, x => x.Type == AuthorizationConstants.EventPermissionClaimType);
        Assert.Equal(evt.Id, EventPermissionClaim.FromString(claim.Value).EventId);
    }

    [Fact]
    public async Task A_published_template_grants_every_user_ViewEventTemplate()
    {
        var template = TestData.EventTemplate(published: true);
        var user = TestData.User();
        await Seed(template, user);

        var principal = await Claims(new ClaimsTransformationOptions(), Token(user.Id));

        var claim = EventTemplatePermissionClaim.FromString(
            Assert.Single(principal.Claims, x => x.Type == AuthorizationConstants.EventTemplatePermissionClaimType).Value);
        Assert.Equal(template.Id, claim.EventTemplateId);
        Assert.Equal([EventTemplatePermission.ViewEventTemplate], claim.Permissions);
    }

    [Fact]
    public async Task A_subject_with_no_user_row_gets_one_named_from_the_token()
    {
        var userId = Guid.NewGuid();

        await Claims(new ClaimsTransformationOptions(), Token(userId, new Claim("name", "First Login")), update: true);

        await using var context = NewContext();
        Assert.Equal("First Login", (await context.Users.SingleAsync(x => x.Id == userId, Ct)).Name);
    }

    [Fact]
    public async Task A_subject_with_no_user_row_gets_no_permission_claims_without_update()
    {
        var principal = await Claims(new ClaimsTransformationOptions(), Token(Guid.NewGuid()));

        Assert.DoesNotContain(principal.Claims, x => x.Type == AuthorizationConstants.PermissionClaimType);
    }

    /// <summary>With caching on, a later change to the user's rows is not seen until the entry expires.</summary>
    [Fact]
    public async Task Cached_claims_answer_until_they_expire()
    {
        var user = TestData.User();
        await Seed(user);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var options = new ClaimsTransformationOptions { EnableCaching = true, CacheExpirationSeconds = 60 };
        await Claims(options, Token(user.Id), cache: cache);
        await using (var context = NewContext())
        {
            (await context.Users.SingleAsync(x => x.Id == user.Id, Ct)).RoleId = TestData.Roles.Administrator;
            await context.SaveChangesAsync(Ct);
        }

        var principal = await Claims(options, Token(user.Id), cache: cache);

        Assert.Empty(SystemPermissions(principal));
    }

    private async Task<ClaimsPrincipal> Claims(
        ClaimsTransformationOptions options,
        ClaimsPrincipal token,
        bool update = false,
        IMemoryCache cache = null)
    {
        await using var context = NewContext();
        using var own = new MemoryCache(new MemoryCacheOptions());

        return await new UserClaimsService(context, cache ?? own, options).AddUserClaims(token, update);
    }

    private static ClaimsPrincipal Token(Guid userId, params Claim[] claims) =>
        new(new ClaimsIdentity([new Claim("sub", userId.ToString()), .. claims], "Test"));

    private static string[] SystemPermissions(ClaimsPrincipal principal) =>
        [.. principal.Claims.Where(x => x.Type == AuthorizationConstants.PermissionClaimType).Select(x => x.Value).Order()];
}
