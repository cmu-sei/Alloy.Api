// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// The seeded ids come from the HasData calls in Alloy.Api.Data/Models (SystemRole.cs, EventRole.cs,
// EventTemplateRole.cs), which the migrations carry. SeedData in appsettings.json is empty and runs only
// in InitializeDatabase, which the template database never sees.

using System;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;

namespace Alloy.Api.Tests.Support;

/// <summary>Object mothers, and the ids of the rows the migrations seed.</summary>
public static class TestData
{
    /// <summary>Ids of the system roles the migrations seed.</summary>
    public static class Roles
    {
        /// <summary><c>AllPermissions</c>, <c>Immutable</c>.</summary>
        public static readonly Guid Administrator = SystemRoleEntityDefaults.AdministratorRoleId;

        /// <summary><c>CreateEventTemplates</c>, <c>CreateEvents</c>, <c>ExecuteEvents</c>.</summary>
        public static readonly Guid ContentDeveloper = SystemRoleEntityDefaults.ContentDeveloperRoleId;

        /// <summary>Every <c>View*</c> system permission.</summary>
        public static readonly Guid Observer = SystemRoleEntityDefaults.ObserverRoleId;
    }

    /// <summary>Ids of the event roles the migrations seed.</summary>
    public static class EventRoles
    {
        /// <summary><c>AllPermissions</c>; what a launch gives the launching user.</summary>
        public static readonly Guid Manager = EventRoleDefaults.EventCreatorRoleId;

        /// <summary><c>ViewEvent</c>.</summary>
        public static readonly Guid Observer = EventRoleDefaults.EventReadOnlyRoleId;

        /// <summary><c>ViewEvent</c>, <c>EditEvent</c>; the column default of every event membership.</summary>
        public static readonly Guid Member = EventRoleDefaults.EventMemberRoleId;
    }

    /// <summary>Ids of the event template roles the migrations seed (the same ids as the event roles).</summary>
    public static class EventTemplateRoles
    {
        /// <summary><c>AllPermissions</c>.</summary>
        public static readonly Guid Manager = EventTemplateRoleEntityDefaults.EventTemplateCreatorRoleId;

        /// <summary><c>ViewEventTemplate</c>.</summary>
        public static readonly Guid Observer = EventTemplateRoleEntityDefaults.EventTemplateReadOnlyRoleId;

        /// <summary><c>ViewEventTemplate</c>, <c>EditEventTemplate</c>; the column default of every membership.</summary>
        public static readonly Guid Member = EventTemplateRoleEntityDefaults.EventTemplateMemberRoleId;
    }

    /// <summary>A fixed timestamp, UTC because every date column is <c>timestamp with time zone</c>.</summary>
    public static readonly DateTime DefaultDate = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static UserEntity User(Guid? id = null, string name = "Test User", Guid? roleId = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            RoleId = roleId
        };

    /// <summary>A system role of its own, named uniquely unless a name is given: role names are uniquely indexed.</summary>
    public static SystemRoleEntity SystemRole(
        string name = null,
        bool allPermissions = false,
        SystemPermission[] permissions = null,
        bool immutable = false)
    {
        var id = Guid.NewGuid();

        return new SystemRoleEntity
        {
            Id = id,
            Name = name ?? $"role-{id:N}",
            AllPermissions = allPermissions,
            Immutable = immutable,
            Permissions = [.. permissions ?? []]
        };
    }

    /// <summary>An event role granting exactly <paramref name="permissions"/>.</summary>
    public static EventRoleEntity EventRole(params EventPermission[] permissions)
    {
        var id = Guid.NewGuid();

        return new EventRoleEntity
        {
            Id = id,
            Name = $"event-role-{id:N}",
            AllPermissions = false,
            Permissions = [.. permissions]
        };
    }

    /// <summary>An event template role granting exactly <paramref name="permissions"/>.</summary>
    public static EventTemplateRoleEntity EventTemplateRole(params EventTemplatePermission[] permissions)
    {
        var id = Guid.NewGuid();

        return new EventTemplateRoleEntity
        {
            Id = id,
            Name = $"event-template-role-{id:N}",
            AllPermissions = false,
            Permissions = [.. permissions]
        };
    }

    /// <summary>
    /// An unpublished template. A published one grants <c>ViewEventTemplate</c> to every user
    /// (<c>UserClaimsService.GetPermissionClaims</c>), so publish only where that is the subject.
    /// </summary>
    public static EventTemplateEntity EventTemplate(
        string name = "Template",
        Guid? viewId = null,
        bool published = false,
        int durationHours = 1) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            ViewId = viewId,
            IsPublished = published,
            DurationHours = durationHours
        };

    public static EventEntity Event(
        Guid? eventTemplateId,
        EventStatus status = EventStatus.Active,
        InternalEventStatus internalStatus = InternalEventStatus.Launched,
        Guid? userId = null,
        string name = "Event") =>
        new()
        {
            Id = Guid.NewGuid(),
            EventTemplateId = eventTemplateId,
            UserId = userId ?? Guid.NewGuid(),
            Username = "user",
            Name = name,
            Status = status,
            InternalStatus = internalStatus,
            StatusDate = DefaultDate
        };

    public static GroupEntity Group(string name = "Group") =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name
        };

    public static GroupMembershipEntity GroupMembership(
        Guid groupId,
        Guid userId,
        GroupMembershipRole role = GroupMembershipRole.Member) =>
        new(groupId, userId, role) { Id = Guid.NewGuid() };

    /// <summary>A membership of a user (or, with <paramref name="groupId"/>, a group) holding the role named.</summary>
    public static EventMembershipEntity EventMembership(Guid eventId, Guid roleId, Guid? userId = null, Guid? groupId = null) =>
        new(eventId, userId, groupId) { Id = Guid.NewGuid(), RoleId = roleId };

    /// <summary>A membership of a user (or, with <paramref name="groupId"/>, a group) holding the role named.</summary>
    public static EventTemplateMembershipEntity EventTemplateMembership(
        Guid eventTemplateId,
        Guid roleId,
        Guid? userId = null,
        Guid? groupId = null) =>
        new(eventTemplateId, userId, groupId) { Id = Guid.NewGuid(), RoleId = roleId };
}
