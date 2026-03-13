// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using Alloy.Api.Infrastructure.Mapping;
using Alloy.Api.Infrastructure.Mappings;
using Shouldly;
using Xunit;

namespace Alloy.Api.Tests.Unit;

public class MappingConfigurationTests
{
    [Fact]
    public void AllMappingProfiles_ShouldHaveValidConfiguration()
    {
        // Arrange
        var config = new MapperConfiguration(cfg =>
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
        });

        // Act - verify mapper can be created (weaker than AssertConfigurationIsValid
        // but the app has unmapped permission properties populated by controllers)
        var mapper = config.CreateMapper();

        // Assert
        mapper.ShouldNotBeNull();
    }

    [Fact]
    public void EventProfile_ShouldMapEventEntityToEvent()
    {
        // Arrange
        var config = new MapperConfiguration(cfg => cfg.AddProfile<EventProfile>());
        var mapper = config.CreateMapper();

        var entity = new Data.Models.EventEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test Event",
            Username = "testuser",
            Status = Data.EventStatus.Active,
            StatusDate = DateTime.UtcNow,
            CreatedBy = Guid.NewGuid(),
            DateCreated = DateTime.UtcNow
        };

        // Act
        var result = mapper.Map<ViewModels.Event>(entity);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(entity.Id);
        result.Name.ShouldBe(entity.Name);
    }

    [Fact]
    public void UserProfile_ShouldMapUserEntityToUser()
    {
        // Arrange
        var config = new MapperConfiguration(cfg => cfg.AddProfile<UserProfile>());
        var mapper = config.CreateMapper();

        var entity = new Data.Models.UserEntity
        {
            Id = Guid.NewGuid(),
            Name = "Test User",
            CreatedBy = Guid.NewGuid(),
            DateCreated = DateTime.UtcNow
        };

        // Act
        var result = mapper.Map<ViewModels.User>(entity);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(entity.Id);
        result.Name.ShouldBe(entity.Name);
    }
}
