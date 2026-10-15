// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// Mirrors UserClaimsService.GetPermissionClaims: system permissions from User.RoleId -> SystemRole
// { AllPermissions, Permissions }; event and event template permissions from the membership's role, for
// memberships of the user or of a group the user belongs to; ManageMembership on a group the user manages.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;

namespace Alloy.Api.Tests.Support;

/// <summary>A seeded user, and the ids of the resources seeded for them.</summary>
/// <remarks>
/// An HTTP test acts as an actor rather than as a hand-built principal: <c>ApiTestBase.Client(actor)</c>
/// puts the id on the request and the real <c>AuthorizationClaimsTransformer</c> derives the permission
/// claims from these rows. What the actor may do is therefore a property of the database.
/// </remarks>
public sealed class TestActor
{
    public required Guid Id { get; init; }

    /// <summary>
    /// Sent as the <c>name</c> claim, which <c>UserClaimsService.ValidateUser</c> writes back to the user
    /// row and <c>EventService</c> reads as the launching user's name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>The events <see cref="TestActorBuilder.OnNewEvent"/> minted, in call order.</summary>
    public required IReadOnlyList<Guid> NewEvents { get; init; }

    /// <summary>The templates <see cref="TestActorBuilder.OnNewEventTemplate"/> minted, in call order.</summary>
    public required IReadOnlyList<Guid> NewEventTemplates { get; init; }

    /// <summary>The groups <see cref="TestActorBuilder.OnNewGroup"/> minted, in call order.</summary>
    public required IReadOnlyList<Guid> NewGroups { get; init; }
}

/// <summary>
/// Seeds a user, the role that grants their system permissions, and their event, event template and
/// group memberships, so that the real claims transformer derives the permissions a test needs.
/// </summary>
/// <remarks>
/// <para>
/// Roles are minted per actor rather than shared: system role names are uniquely indexed, and a minted
/// role grants exactly what the test names. Where a seeded role says what a test means
/// (<c>TestData.Roles.ContentDeveloper</c>, <c>TestData.EventRoles.Manager</c>), pass its id instead.
/// </para>
/// <para>
/// A membership always names its role. The membership tables default <c>role_id</c> to the seeded
/// <c>Member</c> role (<c>ViewEvent</c> and <c>EditEvent</c>, or <c>ViewEventTemplate</c> and
/// <c>EditEventTemplate</c>), so a membership seeded without one would hold more than its test says.
/// </para>
/// <para>
/// <c>OnNew*</c> mints the resource as well, for the near miss of a denied test: the right permission on
/// another event, template or group. A minted template is unpublished, because a published template
/// grants <c>ViewEventTemplate</c> to every user.
/// </para>
/// </remarks>
public sealed class TestActorBuilder(AlloyContext db, CancellationToken ct)
{
    private readonly List<PendingMembership> _memberships = [];
    private readonly List<(Guid GroupId, GroupMembershipRole Role, bool Mint)> _groups = [];
    private Guid _id = Guid.NewGuid();
    private string _name = "Test Actor";
    private Guid? _roleId;
    private SystemPermission[] _systemPermissions;

    /// <summary>Fixes the actor's id, for a test that needs to know it before seeding.</summary>
    public TestActorBuilder WithId(Guid id)
    {
        _id = id;
        return this;
    }

    public TestActorBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>Gives the actor an existing system role, such as <c>TestData.Roles.Administrator</c>.</summary>
    public TestActorBuilder WithRole(Guid roleId)
    {
        if (_systemPermissions is not null)
        {
            throw new InvalidOperationException(
                "WithRole and WithSystemPermissions both decide the actor's system role. Drop one.");
        }

        _roleId = roleId;
        return this;
    }

    /// <summary>Every system permission, by way of the seeded <c>Administrator</c> role (<c>AllPermissions</c>).</summary>
    public TestActorBuilder WithAllSystemPermissions() => WithRole(TestData.Roles.Administrator);

    /// <summary>Exactly these system permissions, by way of a role minted for this actor.</summary>
    public TestActorBuilder WithSystemPermissions(params SystemPermission[] permissions)
    {
        if (_roleId is not null)
        {
            throw new InvalidOperationException(
                "WithSystemPermissions and WithRole both decide the actor's system role. Drop one.");
        }

        _systemPermissions = permissions;
        return this;
    }

    /// <summary>
    /// A membership on an existing event, holding either <paramref name="permissions"/> (a role minted for
    /// it) or the role <paramref name="roleId"/>. With <paramref name="throughGroup"/> the membership
    /// belongs to a group minted for it, which the actor joins as a member: the transformer's group path.
    /// </summary>
    public TestActorBuilder OnEvent(
        Guid eventId,
        EventPermission[] permissions = null,
        Guid? roleId = null,
        bool throughGroup = false)
    {
        _memberships.Add(PendingMembership.ForEvent(eventId, Role(permissions, roleId), throughGroup, mint: false));
        return this;
    }

    /// <summary>As <see cref="OnEvent"/>, for an existing event template.</summary>
    public TestActorBuilder OnEventTemplate(
        Guid eventTemplateId,
        EventTemplatePermission[] permissions = null,
        Guid? roleId = null,
        bool throughGroup = false)
    {
        _memberships.Add(PendingMembership.ForTemplate(eventTemplateId, Role(permissions, roleId), throughGroup, mint: false));
        return this;
    }

    /// <summary>Puts the actor in an existing group. A <c>Manager</c> holds <c>ManageMembership</c> on it.</summary>
    public TestActorBuilder InGroup(Guid groupId, GroupMembershipRole role = GroupMembershipRole.Member)
    {
        _groups.Add((groupId, role, false));
        return this;
    }

    /// <summary>
    /// Puts the actor on a new event, of <paramref name="eventTemplateId"/> when given (which must already
    /// be saved), holding exactly <paramref name="permissions"/>: the near miss of a denied test. The id is
    /// on <see cref="TestActor.NewEvents"/>.
    /// </summary>
    public TestActorBuilder OnNewEvent(Guid? eventTemplateId = null, params EventPermission[] permissions)
    {
        _memberships.Add(PendingMembership.ForEvent(eventTemplateId ?? Guid.Empty, (permissions, null), throughGroup: false, mint: true));
        return this;
    }

    /// <summary>
    /// Puts the actor on a new, unpublished event template holding exactly <paramref name="permissions"/>.
    /// The id is on <see cref="TestActor.NewEventTemplates"/>.
    /// </summary>
    public TestActorBuilder OnNewEventTemplate(params EventTemplatePermission[] permissions)
    {
        _memberships.Add(PendingMembership.ForTemplate(Guid.Empty, (permissions, null), throughGroup: false, mint: true));
        return this;
    }

    /// <summary>
    /// Puts the actor in a new group with no event or template memberships, so it grants only what the
    /// membership role does. The id is on <see cref="TestActor.NewGroups"/>.
    /// </summary>
    public TestActorBuilder OnNewGroup(GroupMembershipRole role = GroupMembershipRole.Member)
    {
        _groups.Add((Guid.Empty, role, true));
        return this;
    }

    /// <summary>Writes the actor and everything above to the database.</summary>
    public async Task<TestActor> SeedAsync()
    {
        var roleId = _roleId;

        if (_systemPermissions is not null)
        {
            var role = TestData.SystemRole(permissions: _systemPermissions);
            db.SystemRoles.Add(role);
            roleId = role.Id;
        }

        db.Users.Add(TestData.User(_id, _name, roleId));

        List<Guid> newEvents = [];
        List<Guid> newTemplates = [];
        List<Guid> newGroups = [];

        foreach (var (groupId, role, mint) in _groups)
        {
            var id = groupId;

            if (mint)
            {
                var group = TestData.Group($"group-{Guid.NewGuid():N}");
                db.Groups.Add(group);
                id = group.Id;
                newGroups.Add(id);
            }

            db.GroupMemberships.Add(TestData.GroupMembership(id, _id, role));
        }

        foreach (var membership in _memberships)
        {
            Guid? groupId = null;

            if (membership.ThroughGroup)
            {
                var group = TestData.Group($"group-{Guid.NewGuid():N}");
                db.Groups.Add(group);
                db.GroupMemberships.Add(TestData.GroupMembership(group.Id, _id));
                groupId = group.Id;
            }

            var userId = groupId is null ? _id : (Guid?)null;

            if (membership.IsEvent)
            {
                var eventId = membership.ResourceId;

                if (membership.Mint)
                {
                    var minted = TestData.Event(eventId == Guid.Empty ? null : eventId, name: $"event-{Guid.NewGuid():N}");
                    db.Events.Add(minted);
                    eventId = minted.Id;
                    newEvents.Add(eventId);
                }

                var eventRoleId = membership.RoleId;

                if (eventRoleId is null)
                {
                    var role = TestData.EventRole(membership.EventPermissions ?? []);
                    db.EventRoles.Add(role);
                    eventRoleId = role.Id;
                }

                db.EventMemberships.Add(TestData.EventMembership(eventId, eventRoleId.Value, userId, groupId));
            }
            else
            {
                var templateId = membership.ResourceId;

                if (membership.Mint)
                {
                    var minted = TestData.EventTemplate($"template-{Guid.NewGuid():N}");
                    db.EventTemplates.Add(minted);
                    templateId = minted.Id;
                    newTemplates.Add(templateId);
                }

                var templateRoleId = membership.RoleId;

                if (templateRoleId is null)
                {
                    var role = TestData.EventTemplateRole(membership.TemplatePermissions ?? []);
                    db.EventTemplateRoles.Add(role);
                    templateRoleId = role.Id;
                }

                db.EventTemplateMemberships.Add(TestData.EventTemplateMembership(templateId, templateRoleId.Value, userId, groupId));
            }
        }

        await db.SaveChangesAsync(ct);

        return new TestActor
        {
            Id = _id,
            Name = _name,
            NewEvents = newEvents,
            NewEventTemplates = newTemplates,
            NewGroups = newGroups
        };
    }

    private static (T[] Permissions, Guid? RoleId) Role<T>(T[] permissions, Guid? roleId)
    {
        if ((permissions is null) == (roleId is null))
        {
            throw new InvalidOperationException(
                "A membership names exactly one of its permissions or an existing role id. The column " +
                "default is the seeded Member role, which grants more than a test usually says.");
        }

        return (permissions, roleId);
    }

    private sealed record PendingMembership(
        bool IsEvent,
        Guid ResourceId,
        EventPermission[] EventPermissions,
        EventTemplatePermission[] TemplatePermissions,
        Guid? RoleId,
        bool ThroughGroup,
        bool Mint)
    {
        public static PendingMembership ForEvent(
            Guid eventId, (EventPermission[] Permissions, Guid? RoleId) role, bool throughGroup, bool mint) =>
            new(true, eventId, role.Permissions, null, role.RoleId, throughGroup, mint);

        public static PendingMembership ForTemplate(
            Guid templateId, (EventTemplatePermission[] Permissions, Guid? RoleId) role, bool throughGroup, bool mint) =>
            new(false, templateId, null, role.Permissions, role.RoleId, throughGroup, mint);
    }
}
