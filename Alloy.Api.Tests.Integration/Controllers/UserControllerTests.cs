// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Tests.Integration.Fixtures;
using Alloy.Api.ViewModels;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Alloy.Api.Tests.Integration.Controllers;

[Category("Integration")]
[ClassDataSource<AlloyTestContext>(Shared = SharedType.PerTestSession)]
public class UserControllerTests(AlloyTestContext context)
{
    [Test]
    public async Task GetUsers_WhenCalled_ReturnsOk()
    {
        // Arrange
        var client = context.CreateClient();

        // Act
        var response = await client.GetAsync("/api/users");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task CreateUser_WithValidUser_ReturnsCreated()
    {
        // Arrange
        var client = context.CreateClient();
        var user = new ViewModels.User
        {
            Id = Guid.NewGuid(),
            Name = "Integration Test User"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/users", user);

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);

        var createdUser = await response.Content.ReadFromJsonAsync<ViewModels.User>();
        await Assert.That(createdUser).IsNotNull();
        await Assert.That(createdUser!.Name).IsEqualTo("Integration Test User");
    }

    [Test]
    public async Task GetUser_WithExistingUserId_ReturnsCorrectUser()
    {
        // Arrange
        var client = context.CreateClient();
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
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var fetchedUser = await response.Content.ReadFromJsonAsync<ViewModels.User>();
        await Assert.That(fetchedUser).IsNotNull();
        await Assert.That(fetchedUser!.Id).IsEqualTo(userId);
        await Assert.That(fetchedUser.Name).IsEqualTo("Lookup Test User");
    }
}
