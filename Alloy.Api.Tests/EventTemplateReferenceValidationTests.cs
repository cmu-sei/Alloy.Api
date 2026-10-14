using System;
using System.Threading.Tasks;
using Alloy.Api.Data.Models;
using Alloy.Api.Infrastructure.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alloy.Api.Tests;

/// <summary>
/// An Event Template can reference only a Caster Directory and a Steamfitter Scenario Template that the
/// author can see, because at launch Alloy's service account deploys and clones whatever they reference.
/// Only new or changed references are checked, so collaborators can still edit other fields.
/// </summary>
[Collection("Postgres")]
public class EventTemplateReferenceValidationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task CreateRejectsADirectoryTheCallerCannotSee()
    {
        using var env = new TestEnvironment(postgres);
        using var db = env.Context();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            env.EventTemplateService(db, new StubPlayerService(), StubCasterService.Forbidden())
                .CreateAsync(new ViewModels.EventTemplate { Name = "Template", DurationHours = 1, DirectoryId = Guid.NewGuid() }, default));

        Assert.Contains("Caster Directory", ex.Message);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, ex.GetStatusCode());
        Assert.Empty(await db.EventTemplates.ToListAsync());
    }

    [Fact]
    public async Task CreateRejectsAScenarioTemplateTheCallerCannotSee()
    {
        using var env = new TestEnvironment(postgres);
        using var db = env.Context();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            env.EventTemplateService(db, new StubPlayerService(), steamfitter: StubSteamfitterService.Forbidden())
                .CreateAsync(new ViewModels.EventTemplate { Name = "Template", DurationHours = 1, ScenarioTemplateId = Guid.NewGuid() }, default));

        Assert.Contains("Steamfitter Scenario Template", ex.Message);
        Assert.Empty(await db.EventTemplates.ToListAsync());
    }

    [Fact]
    public async Task CreateAcceptsReferencesTheCallerCanSee()
    {
        using var env = new TestEnvironment(postgres);
        using var db = env.Context();
        var caster = StubCasterService.Visible();
        var steamfitter = StubSteamfitterService.Visible();
        var directoryId = Guid.NewGuid();
        var scenarioTemplateId = Guid.NewGuid();

        var created = await env.EventTemplateService(db, new StubPlayerService(), caster, steamfitter)
            .CreateAsync(new ViewModels.EventTemplate
            {
                Name = "Template", DurationHours = 1, DirectoryId = directoryId, ScenarioTemplateId = scenarioTemplateId
            }, default);

        Assert.Equal(directoryId, created.DirectoryId);
        Assert.Equal(scenarioTemplateId, created.ScenarioTemplateId);
        Assert.Equal(1, caster.GetDirectoryCalls);
        Assert.Equal(1, steamfitter.GetScenarioTemplateCalls);
    }

    [Fact]
    public async Task UpdateOfOtherFieldsDoesNotRecheckUnchangedReferences()
    {
        using var env = new TestEnvironment(postgres);
        var stored = await SeedTemplate(env);
        using var db = env.Context();
        // Both stubs refuse the caller, like a collaborator who cannot see the author's resources.
        var caster = StubCasterService.Forbidden();
        var steamfitter = StubSteamfitterService.Forbidden();

        await env.EventTemplateService(db, new StubPlayerService(), caster, steamfitter)
            .UpdateAsync(stored.Id, new ViewModels.EventTemplate
            {
                Id = stored.Id, Name = "Renamed", DurationHours = 1,
                DirectoryId = stored.DirectoryId, ScenarioTemplateId = stored.ScenarioTemplateId
            }, default);

        using var read = env.Context();
        Assert.Equal("Renamed", (await read.EventTemplates.SingleAsync()).Name);
        Assert.Equal(0, caster.GetDirectoryCalls);
        Assert.Equal(0, steamfitter.GetScenarioTemplateCalls);
    }

    [Fact]
    public async Task UpdateRejectsAChangedReferenceTheCallerCannotSee()
    {
        using var env = new TestEnvironment(postgres);
        var stored = await SeedTemplate(env);
        using var db = env.Context();

        await Assert.ThrowsAsync<ValidationException>(() =>
            env.EventTemplateService(db, new StubPlayerService(), StubCasterService.Visible(), StubSteamfitterService.Forbidden())
                .UpdateAsync(stored.Id, new ViewModels.EventTemplate
                {
                    Id = stored.Id, Name = "Renamed", DurationHours = 1,
                    DirectoryId = stored.DirectoryId, ScenarioTemplateId = Guid.NewGuid()
                }, default));

        using var read = env.Context();
        var after = await read.EventTemplates.SingleAsync();
        Assert.Equal("Template", after.Name);
        Assert.Equal(stored.ScenarioTemplateId, after.ScenarioTemplateId);
    }

    [Fact]
    public async Task UpdateAcceptsClearingAReference()
    {
        using var env = new TestEnvironment(postgres);
        var stored = await SeedTemplate(env);
        using var db = env.Context();

        await env.EventTemplateService(db, new StubPlayerService(), StubCasterService.Forbidden(), StubSteamfitterService.Forbidden())
            .UpdateAsync(stored.Id, new ViewModels.EventTemplate
            {
                Id = stored.Id, Name = "Template", DurationHours = 1, DirectoryId = null, ScenarioTemplateId = null
            }, default);

        using var read = env.Context();
        var after = await read.EventTemplates.SingleAsync();
        Assert.Null(after.DirectoryId);
        Assert.Null(after.ScenarioTemplateId);
    }

    [Fact]
    public async Task CancellationIsNotReportedAsAnInvalidReference()
    {
        using var env = new TestEnvironment(postgres);
        using var db = env.Context();
        var caster = new StubCasterService { OnGetDirectory = _ => throw new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            env.EventTemplateService(db, new StubPlayerService(), caster)
                .CreateAsync(new ViewModels.EventTemplate { Name = "Template", DurationHours = 1, DirectoryId = Guid.NewGuid() }, default));
    }

    private static async Task<EventTemplateEntity> SeedTemplate(TestEnvironment env)
    {
        using var db = env.Context();
        var template = new EventTemplateEntity
        {
            Id = Guid.NewGuid(), Name = "Template", DurationHours = 1,
            DirectoryId = Guid.NewGuid(), ScenarioTemplateId = Guid.NewGuid()
        };
        db.EventTemplates.Add(template);
        await db.SaveChangesAsync();
        return template;
    }
}
