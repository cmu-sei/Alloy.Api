using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Alloy.Api.Controllers;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.Services;
using Alloy.Api.ViewModels;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Alloy.Api.Tests.TestEnvironment;

namespace Alloy.Api.Tests;

/// <summary>
/// Authorization uses the stored record, not an ID the caller supplies, and an empty permission list
/// grants nothing. Every actor is a non-admin with rights only where a test grants them.
/// </summary>
[Collection("Postgres")]
public class AuthorizationTests(PostgresFixture postgres)
{
    private static readonly Guid ManagerRoleId = EventRoleDefaults.EventCreatorRoleId;
    private static readonly Guid MemberRoleId = EventRoleDefaults.EventMemberRoleId;

    [Fact]
    public async Task MembershipUpdateAuthorizesTheStoredEventNotOneNamedInTheBody()
    {
        using var env = new TestEnvironment(postgres);
        var a = await env.Seed();
        var b = await env.Seed();
        var membershipInB = await SeedMembership(env, b.Id, MemberRoleId);
        // Manages A only. Today's exploit names A in the body to promote a membership in B.
        var actor = Actor(EventClaim(a.Id, EventPermission.ManageEvent), EventClaim(b.Id, EventPermission.ViewEvent));
        using var db = env.Context();
        using var authorization = env.Authorization(db, actor);

        await Assert.ThrowsAsync<ForbiddenException>(() => MembershipController(env, db, authorization, actor)
            .Update(membershipInB.Id, new UpdateEventMembershipRequest { RoleId = ManagerRoleId }, default));

        Assert.Equal(MemberRoleId, (await ReadMembership(env, membershipInB.Id)).RoleId);
    }

    [Fact]
    public async Task MembershipUpdateSucceedsForAManagerOfTheStoredEvent()
    {
        using var env = new TestEnvironment(postgres);
        var b = await env.Seed();
        var membership = await SeedMembership(env, b.Id, MemberRoleId);
        var actor = Actor(EventClaim(b.Id, EventPermission.ManageEvent));
        using var db = env.Context();
        using var authorization = env.Authorization(db, actor);

        await MembershipController(env, db, authorization, actor)
            .Update(membership.Id, new UpdateEventMembershipRequest { RoleId = ManagerRoleId }, default);

        var after = await ReadMembership(env, membership.Id);
        Assert.Equal(ManagerRoleId, after.RoleId);
        Assert.Equal(b.Id, after.EventId);
    }

    [Fact]
    public async Task MembershipUpdateOfAMissingMembershipIsDeniedForNonAdmins()
    {
        using var env = new TestEnvironment(postgres);
        var a = await env.Seed();
        var actor = Actor(EventClaim(a.Id, EventPermission.ManageEvent));
        using var db = env.Context();
        using var authorization = env.Authorization(db, actor);

        await Assert.ThrowsAsync<ForbiddenException>(() => MembershipController(env, db, authorization, actor)
            .Update(Guid.NewGuid(), new UpdateEventMembershipRequest { RoleId = ManagerRoleId }, default));
    }

    [Fact]
    public async Task MembershipCreateUsesTheRouteEventNotTheBody()
    {
        using var env = new TestEnvironment(postgres);
        var a = await env.Seed();
        var b = await env.Seed();
        var userId = await SeedUser(env);
        var actor = Actor(EventClaim(a.Id, EventPermission.ManageEvent));
        using var db = env.Context();
        using var authorization = env.Authorization(db, actor);

        await MembershipController(env, db, authorization, actor).CreateMembership(a.Id,
            new EventMembership { EventId = b.Id, UserId = userId, RoleId = MemberRoleId }, default);

        using var read = env.Context();
        var created = await read.EventMemberships.SingleAsync(x => x.UserId == userId);
        Assert.Equal(a.Id, created.EventId);
    }

    [Fact]
    public async Task MembershipCreateInAnUnmanagedRouteEventIsDenied()
    {
        using var env = new TestEnvironment(postgres);
        var a = await env.Seed();
        var b = await env.Seed();
        var userId = await SeedUser(env);
        var actor = Actor(EventClaim(a.Id, EventPermission.ManageEvent));
        using var db = env.Context();
        using var authorization = env.Authorization(db, actor);

        await Assert.ThrowsAsync<ForbiddenException>(() => MembershipController(env, db, authorization, actor)
            .CreateMembership(b.Id, new EventMembership { EventId = a.Id, UserId = userId, RoleId = MemberRoleId }, default));

        using var read = env.Context();
        Assert.False(await read.EventMemberships.AnyAsync(x => x.UserId == userId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnEmptyOrNullSystemListDoesNotAuthorize(bool empty)
    {
        using var env = new TestEnvironment(postgres);
        using var db = env.Context();
        using var authorization = env.Authorization(db, Actor());

        Assert.False(await authorization.Service.AuthorizeAsync(empty ? [] : null, default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnEmptyOrNullScopedListDoesNotAuthorizeEvenWithAClaimForTheResource(bool empty)
    {
        using var env = new TestEnvironment(postgres);
        var a = await env.Seed();
        using var db = env.Context();
        using var authorization = env.Authorization(db, Actor(EventClaim(a.Id, EventPermission.ManageEvent)));

        Assert.False(await authorization.Service.AuthorizeAsync<Event>(a.Id, [], empty ? [] : (EventPermission[])null, default));
        Assert.False(await authorization.Service.AuthorizeAsync<EventTemplate>(a.EventTemplateId, [],
            empty ? [] : (EventTemplatePermission[])null, default));
    }

    [Fact]
    public async Task AScopedPermissionStillAuthorizesWhenTheSystemCheckFails()
    {
        using var env = new TestEnvironment(postgres);
        var a = await env.Seed();
        var b = await env.Seed();
        using var db = env.Context();
        using var authorization = env.Authorization(db, Actor(EventClaim(a.Id, EventPermission.ViewEvent)));

        Assert.True(await authorization.Service.AuthorizeAsync<Event>(a.Id, [SystemPermission.ViewEvents], [EventPermission.ViewEvent], default));
        Assert.False(await authorization.Service.AuthorizeAsync<Event>(b.Id, [SystemPermission.ViewEvents], [EventPermission.ViewEvent], default));
    }

    [Fact]
    public async Task ASystemPermissionStillAuthorizesEveryEvent()
    {
        using var env = new TestEnvironment(postgres);
        var a = await env.Seed();
        using var db = env.Context();
        using var authorization = env.Authorization(db, Actor(SystemClaim(SystemPermission.ManageEvents)));

        Assert.True(await authorization.Service.AuthorizeAsync<Event>(a.Id, [SystemPermission.ManageEvents], [EventPermission.ManageEvent], default));
    }

    [Fact]
    public async Task ATemplateMembershipResolvesToItsOwnTemplate()
    {
        using var env = new TestEnvironment(postgres);
        var a = await env.Seed();
        var b = await env.Seed();
        var membershipInB = await SeedTemplateMembership(env, b.EventTemplateId.Value);
        using var db = env.Context();
        EventTemplatePermission[] manage = [EventTemplatePermission.ManageEventTemplate];

        using (var managerOfB = env.Authorization(db, Actor(EventTemplateClaim(b.EventTemplateId.Value, manage))))
        {
            Assert.True(await managerOfB.Service.AuthorizeAsync<EventTemplateMembership>(membershipInB.Id, [], manage, default));
            Assert.False(await managerOfB.Service.AuthorizeAsync<EventTemplateMembership>(Guid.NewGuid(), [], manage, default));
        }

        using var managerOfA = env.Authorization(db, Actor(EventTemplateClaim(a.EventTemplateId.Value, manage)));
        Assert.False(await managerOfA.Service.AuthorizeAsync<EventTemplateMembership>(membershipInB.Id, [], manage, default));
    }

    private static EventMembershipsController MembershipController(TestEnvironment env, AlloyContext db,
        AlloyAuthorization authorization, ClaimsPrincipal actor) =>
        new(authorization.Service, new EventMembershipService(db, actor, env.Mapper));

    private static async Task<Guid> SeedUser(TestEnvironment env)
    {
        using var db = env.Context();
        var user = new UserEntity { Id = Guid.NewGuid(), Name = "User" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<EventMembershipEntity> SeedMembership(TestEnvironment env, Guid eventId, Guid roleId)
    {
        var userId = await SeedUser(env);
        using var db = env.Context();
        var membership = new EventMembershipEntity { Id = Guid.NewGuid(), EventId = eventId, UserId = userId, RoleId = roleId };
        db.EventMemberships.Add(membership);
        await db.SaveChangesAsync();
        return membership;
    }

    private static async Task<EventTemplateMembershipEntity> SeedTemplateMembership(TestEnvironment env, Guid eventTemplateId)
    {
        var userId = await SeedUser(env);
        using var db = env.Context();
        var membership = new EventTemplateMembershipEntity { Id = Guid.NewGuid(), EventTemplateId = eventTemplateId, UserId = userId };
        db.EventTemplateMemberships.Add(membership);
        await db.SaveChangesAsync();
        return membership;
    }

    private static async Task<EventMembershipEntity> ReadMembership(TestEnvironment env, Guid id)
    {
        using var db = env.Context();
        return await db.EventMemberships.SingleAsync(x => x.Id == id);
    }
}
