// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Tests.Integration.Fixtures;
using Alloy.Api.ViewModels;
using Shouldly;
using Xunit;

namespace Alloy.Api.Tests.Integration.Controllers;

[Trait("Category", "Integration")]
public class UserControllerTests : IClassFixture<AlloyTestContext>
{
    private readonly AlloyTestContext _context;

    public UserControllerTests(AlloyTestContext context)
    {
        _context = context;
    }

    [Fact]
    public async Task GetUsers_WhenCalled_ReturnsOk()
    {
        // Arrange
        var client = _context.CreateClient();

        // Act
        var response = await client.GetAsync("/api/users");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateUser_WithValidUser_ReturnsCreated()
    {
        // Arrange
        var client = _context.CreateClient();
        var user = new ViewModels.User
        {
            Id = Guid.NewGuid(),
            Name = "Integration Test User"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/users", user);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var createdUser = await response.Content.ReadFromJsonAsync<ViewModels.User>();
        createdUser.ShouldNotBeNull();
        createdUser.Name.ShouldBe("Integration Test User");
    }

    [Fact]
    public async Task GetUser_WithExistingUserId_ReturnsCorrectUser()
    {
        // Arrange
        var client = _context.CreateClient();
        var userId = Guid.NewGuid();
        var user = new ViewModels.User
        {
            Id = userId,
            Name = "Lookup Test User"
        };

        var createResponse = await client.PostAsJsonAsync("/api/users", user);
        createResponse.EnsureSuccessStatusCode();

        // Act
        var response = await client.GetAsync($"/api/users/{userId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var fetchedUser = await response.Content.ReadFromJsonAsync<ViewModels.User>();
        fetchedUser.ShouldNotBeNull();
        fetchedUser.Id.ShouldBe(userId);
        fetchedUser.Name.ShouldBe("Lookup Test User");
    }
}
