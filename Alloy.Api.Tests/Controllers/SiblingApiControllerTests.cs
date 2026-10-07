// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Alloy.Api.Tests.Support;
using Microsoft.AspNetCore.Mvc;

namespace Alloy.Api.Tests.Controllers;

/// <summary>
/// <c>PlayerController</c>, <c>CasterController</c> and <c>SteamfitterController</c> relay a list from a
/// sibling API with the caller's own token, so the sibling's permissions decide what comes back; Alloy
/// itself only asks that the caller be signed in. Player answers per user, so its url is one the test owns;
/// Caster's and Steamfitter's lists are not keyed on anything a test mints, so they are read by the token
/// each actor sends.
/// </summary>
public class SiblingApiControllerTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string PlayerApi = "http://localhost:4300/";

    private const string CasterApi = "http://localhost:4309/";

    private const string SteamfitterApi = "http://localhost:4400/";

    [Fact]
    public async Task GetViews_relays_the_callers_views_from_Player_with_the_callers_token()
    {
        var actor = await Actor().SeedAsync();
        var viewId = Guid.NewGuid();
        Factory.OutboundHttp.RespondJson($"{PlayerApi}api/users/{actor.Id}/views", new[] { new { id = viewId, name = "View" } });

        var views = await ReadAsync<List<IdOnly>>(await Client(actor).GetAsync("api/views", Ct));

        Assert.Equal([viewId], views.Select(x => x.Id));
        Assert.Equal($"Bearer {BearerToken(actor)}", Assert.Single(Factory.OutboundHttp.Sent, x => x.Uri == $"{PlayerApi}api/users/{actor.Id}/views").Headers["Authorization"]);
    }

    [Fact]
    public async Task An_unauthenticated_request_for_views_is_unauthorized()
    {
        await AssertStatus(HttpStatusCode.Unauthorized, await Client().GetAsync("api/views", Ct));
    }

    /// <summary>Caster's unarranged 404 is relayed as a 500 (the client fails to read its body); the request carried the caller's token.</summary>
    [Fact]
    public async Task GetDirectories_asks_Caster_with_the_callers_token()
    {
        var actor = await Actor().SeedAsync();

        using var response = await Client(actor).GetAsync("api/directories", Ct);

        var problem = await AssertJsonError(HttpStatusCode.InternalServerError, response);
        Assert.StartsWith("Could not deserialize the response body stream as", problem.Detail);
        var request = Assert.Single(Factory.OutboundHttp.Sent, x => x.Uri.StartsWith($"{CasterApi}api/directories", StringComparison.Ordinal)
            && x.Headers["Authorization"] == $"Bearer {BearerToken(actor)}");
        Assert.Equal("GET", request.Method.Method);
    }

    /// <summary>Steamfitter's unarranged 404 is relayed as a 500 (the client fails to read its body); the request carried the caller's token.</summary>
    [Fact]
    public async Task GetScenarioTemplates_asks_Steamfitter_with_the_callers_token()
    {
        var actor = await Actor().SeedAsync();

        using var response = await Client(actor).GetAsync("api/scenarioTemplates", Ct);

        var problem = await AssertJsonError(HttpStatusCode.InternalServerError, response);
        Assert.StartsWith("Could not deserialize the response body stream as", problem.Detail);
        Assert.Single(Factory.OutboundHttp.Sent, x => x.Uri.StartsWith($"{SteamfitterApi}api/scenariotemplates", StringComparison.OrdinalIgnoreCase)
            && x.Headers["Authorization"] == $"Bearer {BearerToken(actor)}");
    }

    /// <summary>The health routes are anonymous.</summary>
    [Fact]
    public async Task The_liveness_check_answers_an_anonymous_request_with_ok()
    {
        var response = await Client().GetAsync("api/health/live", Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    private sealed record IdOnly(Guid Id);
}
