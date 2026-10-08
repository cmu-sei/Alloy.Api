// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Alloy.Api.Data.Models;
using Alloy.Api.Tests.Support;
using Alloy.Api.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Alloy.Api.Tests.Controllers;

/// <summary>
/// An event template whose Player view has no default team is refused on create and on update, because at
/// launch Alloy would otherwise put participants on the first team of the view that does not look
/// administrative. Player answers at the shipped <c>ClientSettings:urls:playerApi</c>, keyed on a view id
/// each test mints; a view nobody arranged answers 404, which must fail closed.
/// </summary>
public class EventTemplateViewValidationTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string PlayerApi = "http://localhost:4300/";

    [Fact]
    public async Task Create_refuses_a_view_with_no_default_team()
    {
        var viewId = ArrangeView(defaultTeamId: null);

        var response = await RootClient.PostAsJsonAsync("api/eventTemplates", Template(viewId), AlloyJson, Ct);

        var problem = await AssertJsonError(HttpStatusCode.BadRequest, response);
        Assert.Contains("no default team", problem.Title);
        await AssertNoTemplateNamed(TemplateName);
    }

    [Fact]
    public async Task Create_accepts_a_view_with_a_default_team()
    {
        var viewId = ArrangeView(defaultTeamId: Guid.NewGuid());

        var created = await ReadAsync<EventTemplate>(await RootClient.PostAsJsonAsync("api/eventTemplates", Template(viewId), AlloyJson, Ct));

        Assert.Equal(viewId, created.ViewId);
        await using var context = NewContext();
        Assert.Equal(viewId, (await context.EventTemplates.SingleAsync(x => x.Id == created.Id, Ct)).ViewId);
    }

    /// <summary>
    /// Player is not asked at all: the run-wide handler records no request carrying this test's caller
    /// token, which the Player client forwards on every view read.
    /// </summary>
    [Fact]
    public async Task Create_accepts_a_template_with_no_view()
    {
        var created = await ReadAsync<EventTemplate>(await RootClient.PostAsJsonAsync("api/eventTemplates", Template(viewId: null), AlloyJson, Ct));

        Assert.Null(created.ViewId);
        Assert.DoesNotContain(Factory.OutboundHttp.Sent, x => x.Authorization == $"Bearer {BearerToken(Root)}");
    }

    /// <summary>The view is asked for with the caller's own token, so a view the caller cannot see fails too.</summary>
    [Fact]
    public async Task Create_asks_Player_for_the_view_with_the_callers_token()
    {
        var viewId = ArrangeView(defaultTeamId: Guid.NewGuid());

        await RootClient.PostAsJsonAsync("api/eventTemplates", Template(viewId), AlloyJson, Ct);

        var request = Assert.Single(Factory.OutboundHttp.Sent, x => x.Uri == ViewUrl(viewId));
        Assert.Equal($"Bearer {BearerToken(Root)}", request.Headers["Authorization"]);
    }

    [Fact]
    public async Task Create_fails_closed_when_the_view_cannot_be_retrieved()
    {
        var response = await RootClient.PostAsJsonAsync("api/eventTemplates", Template(Guid.NewGuid()), AlloyJson, Ct);

        var problem = await AssertJsonError(HttpStatusCode.BadRequest, response);
        Assert.Contains("Could not verify", problem.Title);
        await AssertNoTemplateNamed(TemplateName);
    }

    [Fact]
    public async Task Update_refuses_a_view_with_no_default_team_and_leaves_the_template_unchanged()
    {
        var existing = TestData.EventTemplate("Original");
        existing.Description = "Original description";
        await Seed(existing);
        var viewId = ArrangeView(defaultTeamId: null);

        var response = await RootClient.PutAsJsonAsync($"api/eventTemplates/{existing.Id}",
            Template(viewId, existing.Id, "Original", "Changed description"), AlloyJson, Ct);

        var problem = await AssertJsonError(HttpStatusCode.BadRequest, response);
        Assert.Contains("no default team", problem.Title);
        var saved = await Stored(existing.Id);
        Assert.Equal(("Original description", null), (saved.Description, saved.ViewId));
    }

    /// <summary>
    /// The decided behaviour: validation runs on every save, so a template saved before the rule existed
    /// cannot be edited at all, even for an unrelated field, until its view is fixed.
    /// </summary>
    [Fact]
    public async Task Update_refuses_a_description_only_edit_of_a_template_whose_view_has_no_default_team()
    {
        var viewId = ArrangeView(defaultTeamId: null);
        var existing = TestData.EventTemplate("Legacy", viewId);
        existing.Description = "Original description";
        await Seed(existing);

        var response = await RootClient.PutAsJsonAsync($"api/eventTemplates/{existing.Id}",
            Template(viewId, existing.Id, "Legacy", "Changed description"), AlloyJson, Ct);

        await AssertJsonError(HttpStatusCode.BadRequest, response);
        Assert.Equal("Original description", (await Stored(existing.Id)).Description);
    }

    [Fact]
    public async Task Update_accepts_a_view_with_a_default_team()
    {
        var existing = TestData.EventTemplate("Original");
        existing.Description = "Original description";
        await Seed(existing);
        var viewId = ArrangeView(defaultTeamId: Guid.NewGuid());

        var updated = await ReadAsync<EventTemplate>(await RootClient.PutAsJsonAsync($"api/eventTemplates/{existing.Id}",
            Template(viewId, existing.Id, "Original", "Changed description"), AlloyJson, Ct));

        Assert.Equal(viewId, updated.ViewId);
        var saved = await Stored(existing.Id);
        Assert.Equal(("Changed description", viewId), (saved.Description, saved.ViewId));
    }

    private const string TemplateName = "Validated Template";

    private static string ViewUrl(Guid viewId) => $"{PlayerApi}api/views/{viewId}";

    /// <summary>A view of Player's at a url of this test's own, with or without a default team.</summary>
    private Guid ArrangeView(Guid? defaultTeamId)
    {
        var viewId = Guid.NewGuid();
        Factory.OutboundHttp.RespondJson(ViewUrl(viewId), new { id = viewId, name = "View", defaultTeamId });

        return viewId;
    }

    private static EventTemplate Template(Guid? viewId, Guid? id = null, string name = TemplateName, string description = null) =>
        new() { Id = id ?? Guid.Empty, Name = name, Description = description, DurationHours = 1, ViewId = viewId };

    private async Task AssertNoTemplateNamed(string name)
    {
        await using var context = NewContext();
        Assert.False(await context.EventTemplates.AnyAsync(x => x.Name == name, Ct));
    }

    private async Task<EventTemplateEntity> Stored(Guid id)
    {
        await using var context = NewContext();

        return await context.EventTemplates.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }
}
