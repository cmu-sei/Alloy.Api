// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using System.Security.Principal;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.Services;
using AutoMapper;
using FakeItEasy;
using Shouldly;
using Xunit;
using Crucible.Common.Testing.Fixtures;

namespace Alloy.Api.Tests.Unit.Services;

[Trait("Category", "Unit")]
public class UserServiceTests
{
    private static readonly Guid TestUserId = Guid.NewGuid();

    private static (UserService Service, AlloyContext Context) BuildService(
        List<UserEntity>? users = null,
        IMapper? mapper = null,
        Guid? actingUserId = null)
    {
        var context = TestDbContextFactory.Create<AlloyContext>();
        if (users != null)
        {
            context.Users.AddRange(users);
            context.SaveChanges();
        }

        var resolvedMapper = mapper ?? A.Fake<IMapper>();
        var userId = actingUserId ?? TestUserId;

        var claims = new List<Claim> { new("sub", userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "Test");
        IPrincipal principal = new ClaimsPrincipal(identity);

        var svc = FakeBuilder.BuildMeA<UserService>(context, resolvedMapper, principal);
        return (svc, context);
    }

    [Fact]
    public async Task GetAsync_WhenUsersExist_ReturnsAllUsers()
    {
        // Arrange
        var users = new List<UserEntity>
        {
            new() { Id = Guid.NewGuid(), Name = "Alice" },
            new() { Id = Guid.NewGuid(), Name = "Bob" }
        };

        var mapper = A.Fake<IMapper>();
        var expected = new List<ViewModels.User>
        {
            new() { Id = users[0].Id, Name = "Alice" },
            new() { Id = users[1].Id, Name = "Bob" }
        };
        A.CallTo(() => mapper.Map<IEnumerable<ViewModels.User>>(A<object>._)).Returns(expected);

        var (sut, _) = BuildService(users, mapper);

        // Act
        var result = await sut.GetAsync(CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Count().ShouldBe(2);
    }

    [Fact]
    public async Task GetAsync_WithValidUserId_ReturnsCorrectUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var users = new List<UserEntity>
        {
            new() { Id = userId, Name = "Alice" },
            new() { Id = Guid.NewGuid(), Name = "Bob" }
        };

        var mapper = A.Fake<IMapper>();
        var expectedUser = new ViewModels.User { Id = userId, Name = "Alice" };
        A.CallTo(() => mapper.Map<ViewModels.User>(A<UserEntity>.That.Matches(u => u.Id == userId)))
            .Returns(expectedUser);

        var (sut, _) = BuildService(users, mapper);

        // Act
        var result = await sut.GetAsync(userId, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(userId);
        result.Name.ShouldBe("Alice");
    }

    [Fact]
    public async Task DeleteAsync_WhenDeletingSelf_ThrowsForbiddenException()
    {
        // Arrange
        var selfId = Guid.NewGuid();
        var users = new List<UserEntity>
        {
            new() { Id = selfId, Name = "Self" }
        };

        var (sut, _) = BuildService(users, actingUserId: selfId);

        // Act & Assert
        await Should.ThrowAsync<ForbiddenException>(
            () => sut.DeleteAsync(selfId, CancellationToken.None));
    }
}
