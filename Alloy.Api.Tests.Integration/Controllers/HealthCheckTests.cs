// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using Alloy.Api.Tests.Integration.Fixtures;
using Shouldly;
using Xunit;

namespace Alloy.Api.Tests.Integration.Controllers;

[Trait("Category", "Integration")]
public class HealthCheckTests : IClassFixture<AlloyTestContext>
{
    private readonly AlloyTestContext _context;

    public HealthCheckTests(AlloyTestContext context)
    {
        _context = context;
    }

    [Fact]
    public async Task GetReadiness_WhenHealthy_ReturnsOk()
    {
        // Arrange
        var client = _context.CreateClient();

        // Act
        var response = await client.GetAsync("/api/health/ready");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetLiveliness_WhenHealthy_ReturnsOk()
    {
        // Arrange
        var client = _context.CreateClient();

        // Act
        var response = await client.GetAsync("/api/health/live");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
