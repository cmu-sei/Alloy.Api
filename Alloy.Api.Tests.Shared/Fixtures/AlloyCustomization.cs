// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoFixture;
using AutoMapper;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Infrastructure.Mapping;
using Alloy.Api.Infrastructure.Mappings;
using Crucible.Common.Testing.Fixtures.SpecimenBuilders;

namespace Alloy.Api.Tests.Shared.Fixtures;

public class AlloyCustomization : ICustomization
{
    public void Customize(IFixture fixture)
    {
        fixture.Behaviors.Add(new OmitOnRecursionBehavior());
        fixture.Customizations.Add(new GuidIdBuilder());
        fixture.Customizations.Add(new DateTimeOffsetBuilder());

        RegisterEntityFactories(fixture);
        RegisterServices(fixture);
    }

    private void RegisterEntityFactories(IFixture fixture)
    {
        var now = DateTime.UtcNow;

        fixture.Register(() => new EventEntity
        {
            Id = fixture.Create<Guid>(),
            UserId = fixture.Create<Guid>(),
            Username = fixture.Create<string>(),
            Name = $"Event {fixture.Create<string>()}",
            Description = fixture.Create<string>(),
            Status = EventStatus.Active,
            InternalStatus = InternalEventStatus.Launched,
            StatusDate = now,
            CreatedBy = fixture.Create<Guid>(),
            DateCreated = now
        });

        fixture.Register(() => new EventTemplateEntity
        {
            Id = fixture.Create<Guid>(),
            Name = $"EventTemplate {fixture.Create<string>()}",
            Description = fixture.Create<string>(),
            DurationHours = 4,
            IsPublished = false,
            UseDynamicHost = false,
            CreatedBy = fixture.Create<Guid>(),
            DateCreated = now
        });

        fixture.Register(() => new UserEntity
        {
            Id = fixture.Create<Guid>(),
            Name = $"User {fixture.Create<string>()}",
            CreatedBy = fixture.Create<Guid>(),
            DateCreated = now
        });

        fixture.Register(() => new GroupEntity
        {
            Id = fixture.Create<Guid>(),
            Name = $"Group {fixture.Create<string>()}",
            Description = fixture.Create<string>()
        });

        fixture.Register(() => new GroupMembershipEntity
        {
            Id = fixture.Create<Guid>(),
            GroupId = fixture.Create<Guid>(),
            UserId = fixture.Create<Guid>()
        });

        fixture.Register(() => new SystemRoleEntity
        {
            Id = fixture.Create<Guid>(),
            Name = $"Role {fixture.Create<string>()}",
            Description = fixture.Create<string>(),
            AllPermissions = false,
            Immutable = false,
            Permissions = []
        });

        fixture.Register(() => new EventTemplateRoleEntity
        {
            Id = fixture.Create<Guid>(),
            Name = $"EventTemplateRole {fixture.Create<string>()}",
            Description = fixture.Create<string>(),
            AllPermissions = false,
            Permissions = []
        });

        fixture.Register(() => new EventRoleEntity
        {
            Id = fixture.Create<Guid>(),
            Name = $"EventRole {fixture.Create<string>()}",
            Description = fixture.Create<string>(),
            AllPermissions = false,
            Permissions = []
        });

        fixture.Register(() => new EventMembershipEntity
        {
            Id = fixture.Create<Guid>(),
            EventId = fixture.Create<Guid>(),
            UserId = fixture.Create<Guid>(),
            RoleId = EventRoleDefaults.EventMemberRoleId
        });

        fixture.Register(() => new EventTemplateMembershipEntity
        {
            Id = fixture.Create<Guid>(),
            EventTemplateId = fixture.Create<Guid>(),
            UserId = fixture.Create<Guid>(),
            RoleId = EventTemplateRoleEntityDefaults.EventTemplateMemberRoleId
        });

        fixture.Register(() => new EventUserEntity
        {
            Id = fixture.Create<Guid>(),
            UserId = fixture.Create<Guid>(),
            EventId = fixture.Create<Guid>(),
            CreatedBy = fixture.Create<Guid>(),
            DateCreated = now
        });

        fixture.Register(() => new PermissionEntity
        {
            Id = fixture.Create<Guid>(),
            Key = fixture.Create<string>(),
            Value = fixture.Create<string>(),
            Description = fixture.Create<string>(),
            ReadOnly = false,
            CreatedBy = fixture.Create<Guid>(),
            DateCreated = now
        });
    }

    private void RegisterServices(IFixture fixture)
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<EventProfile>();
            cfg.AddProfile<EventTemplateProfile>();
            cfg.AddProfile<UserProfile>();
            cfg.AddProfile<GroupProfile>();
            cfg.AddProfile<GroupMembershipProfile>();
            cfg.AddProfile<SystemRoleProfile>();
            cfg.AddProfile<EventRoleProfile>();
            cfg.AddProfile<EventTemplateRoleProfile>();
            cfg.AddProfile<EventMembershipProfile>();
            cfg.AddProfile<EventTemplateMembershipProfile>();
        }).CreateMapper();

        fixture.Register(() => mapper);
    }
}
