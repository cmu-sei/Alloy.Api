// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// api/system-roles needs ViewRoles to read and ManageRoles to create; role names are uniquely indexed, so
// the concurrency probes would collide on a shared database.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Alloy.Api.Tests.Support;

/// <summary>
/// Tests for the HTTP harness itself: a harness that routes to the wrong database or authorizes everything
/// reads as a green suite.
/// </summary>
public class HttpHarnessTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string SharedName = "Http Isolation Probe";

    [Fact]
    public async Task The_swagger_document_is_served()
    {
        await AssertStatus(HttpStatusCode.OK, await Client().GetAsync("/swagger/v1/swagger.json", Ct));
    }

    [Fact]
    public async Task A_request_with_no_identity_is_unauthorized()
    {
        await AssertStatus(HttpStatusCode.Unauthorized, await Client().GetAsync("api/system-roles", Ct));
    }

    /// <summary>The claims transformer ran and derived nothing, rather than the pipeline granting by default.</summary>
    [Fact]
    public async Task An_actor_with_no_permissions_is_forbidden()
    {
        var actor = await Actor().SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/system-roles", Ct));
    }

    /// <summary>The seeded administrator role becomes real claims, and the request reads this test's database.</summary>
    [Fact]
    public async Task Root_reads_what_the_test_seeded()
    {
        var response = await RootClient.GetAsync("api/users", Ct);

        var users = await ReadAsync<List<IdOnly>>(response);
        Assert.Contains(Root.Id, users.Select(x => x.Id));
    }

    [Fact]
    public Task Concurrent_tests_share_the_host_but_not_the_database_first() => CreateSharedNameResource();

    [Fact]
    public Task Concurrent_tests_share_the_host_but_not_the_database_second() => CreateSharedNameResource();

    private async Task CreateSharedNameResource()
    {
        var response = await RootClient.PostAsJsonAsync("api/system-roles", new { name = SharedName, permissions = Array.Empty<string>() }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);

        await using var context = NewContext();
        Assert.Equal(1, await context.SystemRoles.CountAsync(x => x.Name == SharedName, Ct));
    }

    private sealed record IdOnly(Guid Id);
}
