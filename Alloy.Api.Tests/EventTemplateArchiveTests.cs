using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Alloy.Api.Data.Models;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.Services;
using Alloy.Api.ViewModels;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alloy.Api.Tests;

/// <summary>
/// Event Templates export to an archive holding event_templates.json and import back with their ids.
/// References to other applications are kept only if the importer can see them, otherwise matched by
/// name on request, otherwise cleared with a warning.
/// </summary>
[Collection("Postgres")]
public class EventTemplateArchiveTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(ArchiveType.zip, "event_templates.zip")]
    [InlineData(ArchiveType.tgz, "event_templates.tar.gz")]
    public async Task RoundTripPreservesIdsFieldsAndReferences(ArchiveType archiveType, string fileName)
    {
        using var env = new TestEnvironment(postgres);
        var stored = await Seed(env, isPublished: true);
        var player = StubPlayerService.WithDefaultTeam();
        var caster = StubCasterService.Visible();
        var steamfitter = StubSteamfitterService.Visible();

        byte[] archive;
        using (var db = env.Context())
        {
            using var result = await env.EventTemplateArchiveService(db, player, caster, steamfitter)
                .ExportAsync([stored.Id], archiveType, default);
            Assert.Equal(fileName, result.Name);
            archive = ToBytes(result.Data);
        }

        var exported = await ReadExport(archive, fileName);
        var single = Assert.Single(exported);
        Assert.Equal("Good View", single.ViewName);
        Assert.Equal("Directory", single.DirectoryName);
        Assert.Equal("Scenario Template", single.ScenarioTemplateName);

        await Delete(env, stored.Id);

        using (var db = env.Context())
        {
            var import = await env.EventTemplateArchiveService(db, player, caster, steamfitter)
                .ImportAsync(new MemoryStream(archive), fileName, false, default);
            Assert.Empty(import.Failures);
            Assert.Empty(import.Warnings);
            Assert.Single(import.Imported);
        }

        using var read = env.Context();
        var after = await read.EventTemplates.SingleAsync();
        Assert.Equal(stored.Id, after.Id);
        Assert.Equal(stored.Name, after.Name);
        Assert.Equal(stored.Description, after.Description);
        Assert.Equal(stored.DurationHours, after.DurationHours);
        Assert.Equal(stored.UseDynamicHost, after.UseDynamicHost);
        Assert.True(after.IsPublished);
        Assert.Equal(stored.ViewId, after.ViewId);
        Assert.Equal(stored.DirectoryId, after.DirectoryId);
        Assert.Equal(stored.ScenarioTemplateId, after.ScenarioTemplateId);
    }

    [Fact]
    public async Task ExportStillSucceedsWhenAReferenceCannotBeRead()
    {
        using var env = new TestEnvironment(postgres);
        var stored = await Seed(env);
        using var db = env.Context();

        using var result = await env.EventTemplateArchiveService(db, StubPlayerService.Unavailable(),
                StubCasterService.Forbidden(), StubSteamfitterService.Forbidden())
            .ExportAsync([], ArchiveType.zip, default);

        var single = Assert.Single(await ReadExport(ToBytes(result.Data), result.Name));
        Assert.Equal(stored.ViewId, single.ViewId);
        Assert.Null(single.ViewName);
        Assert.Equal(stored.DirectoryId, single.DirectoryId);
        Assert.Null(single.DirectoryName);
    }

    [Fact]
    public async Task ImportDoesNotOverwriteAnExistingId()
    {
        using var env = new TestEnvironment(postgres);
        var stored = await Seed(env);
        var archive = Archive(new EventTemplateExport { Id = stored.Id, Name = "Changed", DurationHours = 2 });
        using var db = env.Context();

        var result = await env.EventTemplateArchiveService(db, new StubPlayerService())
            .ImportAsync(new MemoryStream(archive), "event_templates.zip", false, default);

        var failure = Assert.Single(result.Failures);
        Assert.Equal(ImportEventTemplateFailureType.EventTemplateExists, failure.FailureType);
        Assert.Empty(result.Imported);
        using var read = env.Context();
        Assert.Equal("Template", (await read.EventTemplates.SingleAsync()).Name);
    }

    [Fact]
    public async Task ImportClearsReferencesTheCallerCannotSeeAndUnpublishes()
    {
        using var env = new TestEnvironment(postgres);
        var exported = new EventTemplateExport
        {
            Id = Guid.NewGuid(), Name = "Moved", DurationHours = 1, IsPublished = true,
            ViewId = Guid.NewGuid(), ViewName = "Missing View",
            DirectoryId = Guid.NewGuid(), DirectoryName = "Missing Directory",
            ScenarioTemplateId = Guid.NewGuid(), ScenarioTemplateName = "Missing Scenario Template",
        };
        using var db = env.Context();

        var result = await env.EventTemplateArchiveService(db, StubPlayerService.Unavailable(),
                StubCasterService.Forbidden(), StubSteamfitterService.Forbidden())
            .ImportAsync(new MemoryStream(Archive(exported)), "event_templates.zip", false, default);

        Assert.Empty(result.Failures);
        Assert.Single(result.Imported);
        Assert.Contains(result.Warnings, w => w.Message.Contains("Player View \"Missing View\""));
        Assert.Contains(result.Warnings, w => w.Message.Contains("Caster Directory \"Missing Directory\""));
        Assert.Contains(result.Warnings, w => w.Message.Contains("Steamfitter Scenario Template \"Missing Scenario Template\""));
        Assert.Contains(result.Warnings, w => w.Message.Contains("unpublished"));

        using var read = env.Context();
        var after = await read.EventTemplates.SingleAsync();
        Assert.Equal(exported.Id, after.Id);
        Assert.Null(after.ViewId);
        Assert.Null(after.DirectoryId);
        Assert.Null(after.ScenarioTemplateId);
        Assert.False(after.IsPublished);
    }

    [Fact]
    public async Task ImportClearsAViewWithoutADefaultTeam()
    {
        using var env = new TestEnvironment(postgres);
        var exported = new EventTemplateExport { Id = Guid.NewGuid(), Name = "Moved", DurationHours = 1, ViewId = Guid.NewGuid() };
        using var db = env.Context();

        var result = await env.EventTemplateArchiveService(db, StubPlayerService.WithoutDefaultTeam())
            .ImportAsync(new MemoryStream(Archive(exported)), "event_templates.zip", false, default);

        Assert.Single(result.Imported);
        Assert.Contains(result.Warnings, w => w.Message.Contains("no default team"));
        using var read = env.Context();
        Assert.Null((await read.EventTemplates.SingleAsync()).ViewId);
    }

    [Fact]
    public async Task ImportMatchesReferencesByNameWhenAsked()
    {
        using var env = new TestEnvironment(postgres);
        var viewId = Guid.NewGuid();
        var directoryId = Guid.NewGuid();
        var scenarioTemplateId = Guid.NewGuid();
        var player = new StubPlayerService
        {
            OnGetView = id => id == viewId
                ? Task.FromResult(new Player.Api.Client.View { Id = id, Name = "Range", DefaultTeamId = Guid.NewGuid() })
                : throw new HttpRequestException("Not Found", null, HttpStatusCode.NotFound),
            OnGetViews = () => Task.FromResult<IEnumerable<Player.Api.Client.View>>(
                [new() { Id = viewId, Name = "Range", DefaultTeamId = Guid.NewGuid() }])
        };
        var caster = new StubCasterService
        {
            OnGetDirectory = id => id == directoryId
                ? Task.FromResult(new Caster.Api.Client.Directory { Id = id, Name = "Network" })
                : throw new HttpRequestException("Not Found", null, HttpStatusCode.NotFound),
            OnGetDirectories = () => Task.FromResult<IEnumerable<Caster.Api.Client.Directory>>(
                [new() { Id = directoryId, Name = "Network" }])
        };
        var steamfitter = new StubSteamfitterService
        {
            OnGetScenarioTemplate = id => id == scenarioTemplateId
                ? Task.FromResult(new Steamfitter.Api.Client.ScenarioTemplate { Id = id, Name = "Tasks" })
                : throw new HttpRequestException("Not Found", null, HttpStatusCode.NotFound),
            OnGetScenarioTemplates = () => Task.FromResult<IEnumerable<Steamfitter.Api.Client.ScenarioTemplate>>(
                [new() { Id = scenarioTemplateId, Name = "Tasks" }])
        };
        var exported = new EventTemplateExport
        {
            Id = Guid.NewGuid(), Name = "Moved", DurationHours = 1,
            ViewId = Guid.NewGuid(), ViewName = "Range",
            DirectoryId = Guid.NewGuid(), DirectoryName = "Network",
            ScenarioTemplateId = Guid.NewGuid(), ScenarioTemplateName = "Tasks",
        };
        using var db = env.Context();

        var result = await env.EventTemplateArchiveService(db, player, caster, steamfitter)
            .ImportAsync(new MemoryStream(Archive(exported)), "event_templates.zip", true, default);

        Assert.Empty(result.Failures);
        Assert.Equal(3, result.Warnings.Count(w => w.Message.Contains("Matched by name")));
        using var read = env.Context();
        var after = await read.EventTemplates.SingleAsync();
        Assert.Equal(viewId, after.ViewId);
        Assert.Equal(directoryId, after.DirectoryId);
        Assert.Equal(scenarioTemplateId, after.ScenarioTemplateId);
    }

    [Fact]
    public async Task ImportDoesNotMatchAnAmbiguousName()
    {
        using var env = new TestEnvironment(postgres);
        var caster = new StubCasterService
        {
            OnGetDirectory = _ => throw new HttpRequestException("Not Found", null, HttpStatusCode.NotFound),
            OnGetDirectories = () => Task.FromResult<IEnumerable<Caster.Api.Client.Directory>>(
                [new() { Id = Guid.NewGuid(), Name = "Network" }, new() { Id = Guid.NewGuid(), Name = "Network" }])
        };
        var exported = new EventTemplateExport
        {
            Id = Guid.NewGuid(), Name = "Moved", DurationHours = 1, DirectoryId = Guid.NewGuid(), DirectoryName = "Network"
        };
        using var db = env.Context();

        var result = await env.EventTemplateArchiveService(db, new StubPlayerService(), caster)
            .ImportAsync(new MemoryStream(Archive(exported)), "event_templates.zip", true, default);

        Assert.Contains(result.Warnings, w => w.Message.Contains("more than one"));
        using var read = env.Context();
        Assert.Null((await read.EventTemplates.SingleAsync()).DirectoryId);
    }

    [Fact]
    public async Task ImportReportsAnArchiveWithoutTheExportFile()
    {
        using var env = new TestEnvironment(postgres);
        using var archive = await new ArchiveService().ArchiveData("other", ArchiveType.zip,
            new Dictionary<string, string> { ["views.json"] = "[]" }, default);
        using var db = env.Context();

        var result = await env.EventTemplateArchiveService(db, new StubPlayerService())
            .ImportAsync(archive.Data, archive.Name, false, default);

        var failure = Assert.Single(result.Failures);
        Assert.Contains(EventTemplateArchiveService.ExportFileName, failure.Reason);
    }

    [Fact]
    public async Task ImportRejectsAFileThatIsNotAnArchive()
    {
        using var env = new TestEnvironment(postgres);
        using var db = env.Context();

        await Assert.ThrowsAsync<ValidationException>(() =>
            env.EventTemplateArchiveService(db, new StubPlayerService())
                .ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes("not a zip")), "event_templates.zip", false, default));
    }

    private static async Task<EventTemplateEntity> Seed(TestEnvironment env, bool isPublished = false)
    {
        using var db = env.Context();
        var template = new EventTemplateEntity
        {
            Id = Guid.NewGuid(), Name = "Template", Description = "Description", DurationHours = 3,
            UseDynamicHost = true, IsPublished = isPublished,
            ViewId = Guid.NewGuid(), DirectoryId = Guid.NewGuid(), ScenarioTemplateId = Guid.NewGuid()
        };
        db.EventTemplates.Add(template);
        await db.SaveChangesAsync();
        return template;
    }

    private static async Task Delete(TestEnvironment env, Guid id)
    {
        using var db = env.Context();
        db.EventTemplates.Remove(await db.EventTemplates.SingleAsync(x => x.Id == id));
        await db.SaveChangesAsync();
    }

    private static byte[] Archive(params EventTemplateExport[] templates)
    {
        using var result = new ArchiveService().ArchiveData("event_templates", ArchiveType.zip,
            new Dictionary<string, string>
            {
                [EventTemplateArchiveService.ExportFileName] = System.Text.Json.JsonSerializer.Serialize(templates)
            }, default).GetAwaiter().GetResult();
        return ToBytes(result.Data);
    }

    private static async Task<EventTemplateExport[]> ReadExport(byte[] archive, string fileName)
    {
        var entries = await new ArchiveService().ExtractArchive(new MemoryStream(archive), fileName, default);
        return System.Text.Json.JsonSerializer.Deserialize<EventTemplateExport[]>(entries[EventTemplateArchiveService.ExportFileName]);
    }

    private static byte[] ToBytes(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
