// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Tests.Support;
using Alloy.Api.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Alloy.Api.Tests.Controllers;

/// <summary>
/// An event template may reference only a Caster directory and a Steamfitter scenario template the caller
/// can see, because at launch Alloy's service account deploys and clones whatever it references. Only a new
/// or changed reference is checked, so a collaborator who cannot see the author's resources can still edit
/// the other fields. Caster and Steamfitter answer at the shipped <c>ClientSettings:urls</c>, keyed on ids
/// each test mints; a reference nobody arranged answers 404.
/// </summary>
public class EventTemplateReferenceValidationTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string CasterApi = "http://localhost:4309/";
    private const string SteamfitterApi = "http://localhost:4400/";

    /// <summary>
    /// Caster is asked with the caller's own token, so its refusal is the caller's: Alloy's service account,
    /// which would deploy the directory at launch, is not the one asked.
    /// </summary>
    [Fact]
    public async Task Create_refuses_a_directory_the_caller_cannot_see()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.CreateEventTemplates).SeedAsync();
        var directoryId = ArrangeDirectory(HttpStatusCode.Forbidden);

        var response = await Client(actor).PostAsJsonAsync("api/eventTemplates", Template(directoryId: directoryId), AlloyJson, Ct);

        var problem = await AssertJsonError(HttpStatusCode.BadRequest, response);
        Assert.Contains("Caster Directory", problem.Title);
        await AssertNoTemplates();
        var request = Assert.Single(Factory.OutboundHttp.Sent, x => x.Path == DirectoryPath(directoryId));
        Assert.Equal($"Bearer {BearerToken(actor)}", request.Headers["Authorization"]);
    }

    [Fact]
    public async Task Create_refuses_a_scenario_template_the_caller_cannot_see()
    {
        var scenarioTemplateId = ArrangeScenarioTemplate(HttpStatusCode.Forbidden);

        var response = await RootClient.PostAsJsonAsync("api/eventTemplates", Template(scenarioTemplateId: scenarioTemplateId), AlloyJson, Ct);

        var problem = await AssertJsonError(HttpStatusCode.BadRequest, response);
        Assert.Contains("Steamfitter Scenario Template", problem.Title);
        await AssertNoTemplates();
    }

    /// <summary>A Caster request that times out during the directory check is answered with a 500.</summary>
    [Fact]
    public async Task Create_answers_a_directory_check_that_times_out_with_a_server_error()
    {
        var directoryId = Guid.NewGuid();
        Factory.OutboundHttp.RespondByThrowing(DirectoryUrl(directoryId), new TaskCanceledException(SiblingTimedOut, new TimeoutException()));

        var response = await RootClient.PostAsJsonAsync("api/eventTemplates", Template(directoryId: directoryId), AlloyJson, Ct);

        var problem = await AssertJsonError(HttpStatusCode.InternalServerError, response);
        Assert.Equal(("A server error occurred.", SiblingTimedOut), (problem.Title, problem.Detail));
        await AssertNoTemplates();
    }

    [Fact]
    public async Task Create_accepts_references_the_caller_can_see_asking_each_sibling_once_with_the_callers_token()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.CreateEventTemplates).SeedAsync();
        var directoryId = ArrangeDirectory(HttpStatusCode.OK);
        var scenarioTemplateId = ArrangeScenarioTemplate(HttpStatusCode.OK);

        var created = await ReadAsync<EventTemplate>(await Client(actor).PostAsJsonAsync("api/eventTemplates",
            Template(directoryId: directoryId, scenarioTemplateId: scenarioTemplateId), AlloyJson, Ct));

        Assert.Equal((directoryId, scenarioTemplateId), (created.DirectoryId, created.ScenarioTemplateId));
        var saved = await Stored(created.Id);
        Assert.Equal((directoryId, scenarioTemplateId), (saved.DirectoryId, saved.ScenarioTemplateId));
        var directoryRequest = Assert.Single(Factory.OutboundHttp.Sent, x => x.Path == DirectoryPath(directoryId));
        var scenarioTemplateRequest = Assert.Single(Factory.OutboundHttp.Sent, x => x.Path == ScenarioTemplatePath(scenarioTemplateId));
        Assert.Equal($"Bearer {BearerToken(actor)}", directoryRequest.Headers["Authorization"]);
        Assert.Equal($"Bearer {BearerToken(actor)}", scenarioTemplateRequest.Headers["Authorization"]);
    }

    /// <summary>
    /// The caller is a collaborator holding <c>EditEventTemplate</c>, whom Caster and Steamfitter both refuse;
    /// neither is asked, because neither reference changed.
    /// </summary>
    [Fact]
    public async Task Update_of_other_fields_by_a_collaborator_does_not_recheck_unchanged_references()
    {
        var stored = await SeedTemplateWithReferences(HttpStatusCode.Forbidden, HttpStatusCode.Forbidden);
        var actor = await Actor().OnEventTemplate(stored.Id, [EventTemplatePermission.EditEventTemplate]).SeedAsync();

        var response = await Client(actor).PutAsJsonAsync($"api/eventTemplates/{stored.Id}",
            Template(stored.Id, "Renamed", stored.DirectoryId, stored.ScenarioTemplateId), AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("Renamed", (await Stored(stored.Id)).Name);
        Assert.DoesNotContain(Factory.OutboundHttp.Sent, x => x.Path == DirectoryPath(stored.DirectoryId.Value));
        Assert.DoesNotContain(Factory.OutboundHttp.Sent, x => x.Path == ScenarioTemplatePath(stored.ScenarioTemplateId.Value));
    }

    [Fact]
    public async Task Update_refuses_a_changed_directory_the_caller_cannot_see_and_leaves_the_template_unchanged()
    {
        var stored = await SeedTemplateWithReferences(HttpStatusCode.OK, HttpStatusCode.OK);
        var changed = ArrangeDirectory(HttpStatusCode.Forbidden);

        var response = await RootClient.PutAsJsonAsync($"api/eventTemplates/{stored.Id}",
            Template(stored.Id, "Renamed", changed, stored.ScenarioTemplateId), AlloyJson, Ct);

        var problem = await AssertJsonError(HttpStatusCode.BadRequest, response);
        Assert.Contains("Caster Directory", problem.Title);
        var saved = await Stored(stored.Id);
        Assert.Equal((stored.Name, stored.DirectoryId), (saved.Name, saved.DirectoryId));
    }

    [Fact]
    public async Task Update_refuses_a_changed_reference_the_caller_cannot_see_and_leaves_the_template_unchanged()
    {
        var stored = await SeedTemplateWithReferences(HttpStatusCode.OK, HttpStatusCode.OK);
        var changed = ArrangeScenarioTemplate(HttpStatusCode.Forbidden);

        var response = await RootClient.PutAsJsonAsync($"api/eventTemplates/{stored.Id}",
            Template(stored.Id, "Renamed", stored.DirectoryId, changed), AlloyJson, Ct);

        var problem = await AssertJsonError(HttpStatusCode.BadRequest, response);
        Assert.Contains("Steamfitter Scenario Template", problem.Title);
        var saved = await Stored(stored.Id);
        Assert.Equal((stored.Name, stored.ScenarioTemplateId), (saved.Name, saved.ScenarioTemplateId));
    }

    /// <summary>A null reference is valid, so clearing one asks neither sibling, even where both refuse the caller.</summary>
    [Fact]
    public async Task Update_accepts_clearing_both_references()
    {
        var stored = await SeedTemplateWithReferences(HttpStatusCode.Forbidden, HttpStatusCode.Forbidden);

        var response = await RootClient.PutAsJsonAsync($"api/eventTemplates/{stored.Id}",
            Template(stored.Id, stored.Name, directoryId: null, scenarioTemplateId: null), AlloyJson, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        var saved = await Stored(stored.Id);
        Assert.Equal((null, null), (saved.DirectoryId, saved.ScenarioTemplateId));
    }

    /// <summary>
    /// The message of what <c>HttpClient</c> throws for a request that runs past its <c>Timeout</c> (100
    /// seconds by default): a <c>TaskCanceledException</c> over a <c>TimeoutException</c>, while the caller's
    /// own token is not cancelled.
    /// </summary>
    private const string SiblingTimedOut = "The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.";

    private static string DirectoryPath(Guid id) => $"api/directories/{id}";

    private static string ScenarioTemplatePath(Guid id) => $"api/scenariotemplates/{id}";

    /// <summary>The url the Caster client asks for a directory at, without its related entities or file contents.</summary>
    private static string DirectoryUrl(Guid id) => $"{CasterApi}{DirectoryPath(id)}?IncludeRelated=false&IncludeFileContent=false";

    private static string ScenarioTemplateUrl(Guid id) => $"{SteamfitterApi}{ScenarioTemplatePath(id)}";

    /// <summary>A directory of Caster's at a url of this test's own, visible (200) or refused.</summary>
    private Guid ArrangeDirectory(HttpStatusCode status)
    {
        var id = Guid.NewGuid();
        Factory.OutboundHttp.RespondJson(DirectoryUrl(id), new { id, name = "Directory" }, status);

        return id;
    }

    /// <summary>A scenario template of Steamfitter's at a url of this test's own, visible (200) or refused.</summary>
    private Guid ArrangeScenarioTemplate(HttpStatusCode status)
    {
        var id = Guid.NewGuid();
        Factory.OutboundHttp.RespondJson(ScenarioTemplateUrl(id), new { id, name = "Scenario Template" }, status);

        return id;
    }

    private async Task<EventTemplateEntity> SeedTemplateWithReferences(HttpStatusCode directory, HttpStatusCode scenarioTemplate)
    {
        var template = TestData.EventTemplate();
        template.DirectoryId = ArrangeDirectory(directory);
        template.ScenarioTemplateId = ArrangeScenarioTemplate(scenarioTemplate);
        await Seed(template);

        return template;
    }

    private static EventTemplate Template(
        Guid? id = null,
        string name = "Template",
        Guid? directoryId = null,
        Guid? scenarioTemplateId = null) =>
        new()
        {
            Id = id ?? Guid.Empty,
            Name = name,
            DurationHours = 1,
            DirectoryId = directoryId,
            ScenarioTemplateId = scenarioTemplateId
        };

    private async Task AssertNoTemplates()
    {
        await using var context = NewContext();
        Assert.False(await context.EventTemplates.AnyAsync(Ct));
    }

    private async Task<EventTemplateEntity> Stored(Guid id)
    {
        await using var context = NewContext();

        return await context.EventTemplates.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }
}
