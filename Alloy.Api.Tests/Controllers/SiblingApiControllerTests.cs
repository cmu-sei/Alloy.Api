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
/// Caster's and Steamfitter's urls are fixed and arranged by one test each, whose answer carries an id it
/// minted and whose request is told apart by the token its actor sends.
/// </summary>
public class SiblingApiControllerTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string PlayerApi = "http://localhost:4300/";

    /// <summary>
    /// The url <c>CasterService.GetDirectoriesAsync</c> asks; fixed, so it is arranged only here, and the
    /// answer carries an id the test minted.
    /// </summary>
    private const string CasterDirectories = "http://localhost:4309/api/directories?IncludeRelated=false&IncludeFileContent=false";

    /// <summary>The url <c>SteamfitterService.GetScenarioTemplatesAsync</c> asks; fixed, so it is arranged only here.</summary>
    private const string SteamfitterScenarioTemplates = "http://localhost:4400/api/scenariotemplates";

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

    [Fact]
    public async Task GetDirectories_relays_the_directories_from_Caster_with_the_callers_token()
    {
        var actor = await Actor().SeedAsync();
        var directoryId = Guid.NewGuid();
        Factory.OutboundHttp.RespondJson(CasterDirectories, new[] { new { id = directoryId, name = "Directory" } });

        var directories = await ReadAsync<List<IdOnly>>(await Client(actor).GetAsync("api/directories", Ct));

        Assert.Equal([directoryId], directories.Select(x => x.Id));
        Assert.Equal($"Bearer {BearerToken(actor)}", Assert.Single(Factory.OutboundHttp.Sent, x => x.Uri == CasterDirectories && x.Headers["Authorization"] == $"Bearer {BearerToken(actor)}").Headers["Authorization"]);
    }

    [Fact]
    public async Task GetScenarioTemplates_relays_the_templates_from_Steamfitter_with_the_callers_token()
    {
        var actor = await Actor().SeedAsync();
        var templateId = Guid.NewGuid();
        Factory.OutboundHttp.RespondJson(SteamfitterScenarioTemplates, new[] { new { id = templateId, name = "Scenario Template" } });

        var templates = await ReadAsync<List<IdOnly>>(await Client(actor).GetAsync("api/scenarioTemplates", Ct));

        Assert.Equal([templateId], templates.Select(x => x.Id));
        Assert.Single(Factory.OutboundHttp.Sent, x => x.Uri == SteamfitterScenarioTemplates && x.Headers["Authorization"] == $"Bearer {BearerToken(actor)}");
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
