// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Alloy.Api.Tests.Infrastructure.Filters;

/// <summary>
/// The shapes clients parse: <c>JsonExceptionFilter</c> for exceptions thrown by an action (an
/// <c>IApiException</c>'s status, or a 500 with the message in <c>Detail</c> in Production), and the
/// <c>[ApiController]</c> model-state answer for a request that does not bind.
/// </summary>
public class JsonExceptionFilterTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    /// <summary>A handled exception is a <c>ProblemDetails</c> body sent as <c>application/json</c>, its message in the title.</summary>
    [Fact]
    public async Task A_handled_exception_is_answered_with_its_status_and_message_as_application_json()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEvents).SeedAsync();

        var response = await Client(actor).GetAsync("api/system-roles", Ct);

        var problem = await AssertJsonError(HttpStatusCode.Forbidden, response);
        Assert.Equal(("Insufficient Permissions", null), (problem.Title, problem.Detail));
    }

    [Fact]
    public async Task A_missing_entity_is_answered_with_a_not_found_naming_it()
    {
        var problem = await AssertJsonError(HttpStatusCode.NotFound, await RootClient.GetAsync($"api/users/{Guid.NewGuid()}", Ct));

        Assert.Equal("User not found", problem.Title);
    }

    /// <summary>In Production a 500 carries a fixed title and the exception's message, never its stack trace.</summary>
    // Same case as SystemRoleControllerTests.Create_answers_a_name_that_is_already_taken_with_a_server_error.
    [Fact]
    public async Task An_unhandled_exception_is_answered_with_a_fixed_title_and_the_message_as_detail()
    {
        var problem = await AssertJsonError(HttpStatusCode.InternalServerError,
            await RootClient.PostAsJsonAsync("api/system-roles", new { name = "Administrator", permissions = Array.Empty<string>() }, Ct));

        Assert.Equal("A server error occurred.", problem.Title);
        Assert.DoesNotContain(" at ", problem.Detail);
    }

    [Fact]
    public async Task A_malformed_body_is_a_validation_problem()
    {
        using var body = new StringContent("{ \"name\": ", Encoding.UTF8, "application/json");

        await AssertProblem(HttpStatusCode.BadRequest, await RootClient.PostAsync("api/groups", body, Ct));
    }

    [Fact]
    public async Task A_missing_body_is_a_validation_problem()
    {
        using var body = new StringContent(string.Empty, Encoding.UTF8, "application/json");

        await AssertProblem(HttpStatusCode.BadRequest, await RootClient.PostAsync("api/events", body, Ct));
    }

    [Fact]
    public async Task A_route_id_that_is_not_a_guid_is_a_validation_problem()
    {
        await AssertProblem(HttpStatusCode.BadRequest, await RootClient.GetAsync("api/events/not-a-guid", Ct));
    }

    /// <summary>An integer outside <see cref="EventStatus"/> is accepted and stored.</summary>
    [Fact]
    public async Task An_event_status_outside_the_enum_is_stored()
    {
        const int undefinedStatus = 999;
        var id = Guid.NewGuid();

        var response = await RootClient.PostAsJsonAsync("api/events",
            new { id, name = "Out of range", userId = Guid.NewGuid(), status = undefinedStatus, statusDate = TestData.DefaultDate }, Ct);

        await AssertStatus(HttpStatusCode.Created, response);
        await using var context = NewContext();
        Assert.Equal((EventStatus)undefinedStatus, (await context.Events.SingleAsync(x => x.Id == id, Ct)).Status);
    }

    /// <summary>A request with no identity is answered with a 401; the anonymous client is the case under test.</summary>
    [Fact]
    public async Task An_unauthenticated_request_is_unauthorized()
    {
        await AssertStatus(HttpStatusCode.Unauthorized, await Client().GetAsync("api/events", Ct));
    }
}
