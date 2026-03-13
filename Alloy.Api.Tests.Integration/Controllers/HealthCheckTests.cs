// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using Alloy.Api.Tests.Integration.Fixtures;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Alloy.Api.Tests.Integration.Controllers;

[Category("Integration")]
[ClassDataSource<AlloyTestContext>(Shared = SharedType.PerTestSession)]
public class HealthCheckTests(AlloyTestContext context)
{
    [Test]
    public async Task GetReadiness_WhenHealthy_ReturnsOk()
    {
        // Arrange
        var client = context.CreateClient();

        // Act
        var response = await client.GetAsync("/api/health/ready");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetLiveliness_WhenHealthy_ReturnsOk()
    {
        // Arrange
        var client = context.CreateClient();

        // Act
        var response = await client.GetAsync("/api/health/live");

        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }
}
