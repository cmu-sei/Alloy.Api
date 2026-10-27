// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Services;
using Alloy.Api.Tests.Support;
using Alloy.Api.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace Alloy.Api.Tests.Controllers;

/// <summary>
/// <c>EventTemplateController</c>'s export and import over HTTP: an archive holding
/// <c>event_templates.json</c>, written and read back. A reference to Player, Caster or Steamfitter is kept
/// on import only if the importer can see it, and otherwise cleared with a warning. Player, Caster and
/// Steamfitter answer at the shipped <c>ClientSettings:urls</c>, keyed on ids each test mints; a reference
/// nobody arranged answers 404. Matching a reference by name asks for the sibling's whole list, a url no
/// test owns, so it is tested in <c>Services/EventTemplateArchiveServiceTests</c>.
/// </summary>
public class EventTemplateArchiveTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string PlayerApi = "http://localhost:4300/";
    private const string CasterApi = "http://localhost:4309/";
    private const string SteamfitterApi = "http://localhost:4400/";

    // GET api/eventTemplates/actions/export

    [Fact]
    public async Task Export_of_named_templates_by_a_member_holding_ViewEventTemplate_writes_them()
    {
        var template = await SeedTemplate();
        await SeedTemplate("Other");
        var actor = await Actor().OnEventTemplate(template.Id, [EventTemplatePermission.ViewEventTemplate]).SeedAsync();

        var response = await Client(actor).GetAsync(ExportUrl(template.Id), Ct);

        var exported = Assert.Single(await ReadExport(response, "event_templates.zip"));
        Assert.Equal((template.Id, template.Name), (exported.Id, exported.Name));
    }

    [Fact]
    public async Task Export_is_forbidden_for_a_member_holding_ViewEventTemplate_only_on_another_template()
    {
        var template = await SeedTemplate();
        var actor = await Actor().OnNewEventTemplate(EventTemplatePermission.ViewEventTemplate).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync(ExportUrl(template.Id), Ct));
    }

    /// <summary>One template the caller cannot see refuses the whole export, rather than leaving it out.</summary>
    [Fact]
    public async Task Export_is_forbidden_when_one_named_template_is_not_visible_to_the_member()
    {
        var visible = await SeedTemplate();
        var hidden = await SeedTemplate("Hidden");
        var actor = await Actor().OnEventTemplate(visible.Id, [EventTemplatePermission.ViewEventTemplate]).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync(ExportUrl(visible.Id, hidden.Id), Ct));
    }

    [Fact]
    public async Task Export_of_every_template_by_a_caller_holding_ViewEventTemplates_writes_them_all()
    {
        await SeedTemplate("B");
        await SeedTemplate("A");
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEventTemplates).SeedAsync();

        var response = await Client(actor).GetAsync("api/eventTemplates/actions/export", Ct);

        var exported = await ReadExport(response, "event_templates.zip");
        Assert.Equal(["A", "B"], Array.ConvertAll(exported, x => x.Name));
    }

    /// <summary>A member who can see a template still needs the system permission to export every template.</summary>
    [Fact]
    public async Task Export_of_every_template_is_forbidden_for_a_caller_holding_only_ViewEventTemplate_on_a_template()
    {
        var template = await SeedTemplate();
        var actor = await Actor().WithSystemPermissions(SystemPermission.CreateEventTemplates)
            .OnEventTemplate(template.Id, [EventTemplatePermission.ViewEventTemplate]).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Client(actor).GetAsync("api/eventTemplates/actions/export", Ct));
    }

    [Fact]
    public async Task Export_writes_each_reference_with_its_name()
    {
        var template = TestData.EventTemplate();
        template.ViewId = ArrangeView(Guid.NewGuid(), "Range View");
        template.DirectoryId = ArrangeDirectory(HttpStatusCode.OK, "Range Directory");
        template.ScenarioTemplateId = ArrangeScenarioTemplate(HttpStatusCode.OK, "Range Tasks");
        await Seed(template);

        var exported = Assert.Single(await ReadExport(await RootClient.GetAsync(ExportUrl(template.Id), Ct), "event_templates.zip"));

        Assert.Equal((template.ViewId, "Range View"), (exported.ViewId, exported.ViewName));
        Assert.Equal((template.DirectoryId, "Range Directory"), (exported.DirectoryId, exported.DirectoryName));
        Assert.Equal((template.ScenarioTemplateId, "Range Tasks"), (exported.ScenarioTemplateId, exported.ScenarioTemplateName));
    }

    /// <summary>A name is a convenience for the importer, so a reference whose sibling refuses is still exported by id.</summary>
    [Fact]
    public async Task Export_writes_a_reference_it_cannot_read_by_id_alone()
    {
        var template = TestData.EventTemplate(viewId: Guid.NewGuid());
        template.DirectoryId = ArrangeDirectory(HttpStatusCode.Forbidden);
        template.ScenarioTemplateId = ArrangeScenarioTemplate(HttpStatusCode.Forbidden);
        await Seed(template);

        var exported = Assert.Single(await ReadExport(await RootClient.GetAsync(ExportUrl(template.Id), Ct), "event_templates.zip"));

        Assert.Equal((template.ViewId, null), (exported.ViewId, exported.ViewName));
        Assert.Equal((template.DirectoryId, null), (exported.DirectoryId, exported.DirectoryName));
        Assert.Equal((template.ScenarioTemplateId, null), (exported.ScenarioTemplateId, exported.ScenarioTemplateName));
    }

    // POST api/eventTemplates/actions/import

    [Theory]
    [InlineData(ArchiveType.zip, "event_templates.zip")]
    [InlineData(ArchiveType.tgz, "event_templates.tar.gz")]
    public async Task Import_of_an_export_restores_its_fields_and_visible_references(ArchiveType archiveType, string fileName)
    {
        var template = TestData.EventTemplate(published: true, durationHours: 3);
        template.Description = "Description";
        template.UseDynamicHost = true;
        template.ViewId = ArrangeView(Guid.NewGuid());
        template.DirectoryId = ArrangeDirectory(HttpStatusCode.OK);
        template.ScenarioTemplateId = ArrangeScenarioTemplate(HttpStatusCode.OK);
        await Seed(template);
        var export = await RootClient.GetAsync($"api/eventTemplates/actions/export?ids={template.Id}&archiveType={archiveType}", Ct);
        await AssertStatus(HttpStatusCode.OK, export);
        Assert.Equal(fileName, export.Content.Headers.ContentDisposition?.FileName);
        var archive = await export.Content.ReadAsByteArrayAsync(Ct);
        await DeleteTemplate(template.Id);

        var result = await ReadAsync<ImportEventTemplatesResult>(await Import(RootClient, archive, fileName));

        Assert.Empty(result.Failures);
        Assert.Empty(result.Warnings);
        var imported = Assert.Single(result.Imported);
        var stored = await Stored(imported.Id);
        Assert.Equal(template.Id, stored.Id);
        Assert.Equal((template.Name, template.Description, 3, true, true),
            (stored.Name, stored.Description, stored.DurationHours, stored.UseDynamicHost, stored.IsPublished));
        Assert.Equal((template.ViewId, template.DirectoryId, template.ScenarioTemplateId),
            (stored.ViewId, stored.DirectoryId, stored.ScenarioTemplateId));
    }

    [Fact]
    public async Task Import_by_a_caller_holding_CreateEventTemplates_creates_the_template()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.CreateEventTemplates).SeedAsync();

        var result = await ReadAsync<ImportEventTemplatesResult>(await Import(Client(actor), Archive(Exported("Imported"))));

        var imported = Assert.Single(result.Imported);
        Assert.Equal("Imported", (await Stored(imported.Id)).Name);
    }

    [Fact]
    public async Task Import_is_forbidden_for_a_caller_holding_only_EditEventTemplates()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditEventTemplates).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Import(Client(actor), Archive(Exported("Denied"))));

        await AssertNoTemplates();
    }

    [Fact]
    public async Task Import_does_not_overwrite_a_template_with_the_same_id()
    {
        var template = await SeedTemplate();
        var exported = Exported("Changed");
        exported.Id = template.Id;

        var result = await ReadAsync<ImportEventTemplatesResult>(await Import(RootClient, Archive(exported)));

        var failure = Assert.Single(result.Failures);
        Assert.Equal(ImportEventTemplateFailureType.EventTemplateExists, failure.FailureType);
        Assert.Empty(result.Imported);
        Assert.Equal(template.Name, (await Stored(template.Id)).Name);
    }

    /// <summary>
    /// None of the three references resolves in this environment (each answers 404), so each is cleared, and a
    /// published template that lost one is imported unpublished.
    /// </summary>
    [Fact]
    public async Task Import_clears_references_the_caller_cannot_see_and_unpublishes_the_template()
    {
        var exported = Exported("Moved");
        exported.IsPublished = true;
        (exported.ViewId, exported.ViewName) = (Guid.NewGuid(), "Missing View");
        (exported.DirectoryId, exported.DirectoryName) = (Guid.NewGuid(), "Missing Directory");
        (exported.ScenarioTemplateId, exported.ScenarioTemplateName) = (Guid.NewGuid(), "Missing Scenario Template");

        var result = await ReadAsync<ImportEventTemplatesResult>(await Import(RootClient, Archive(exported)));

        Assert.Empty(result.Failures);
        var imported = Assert.Single(result.Imported);
        Assert.Contains(result.Warnings, x => x.Message.Contains("Player View \"Missing View\""));
        Assert.Contains(result.Warnings, x => x.Message.Contains("Caster Directory \"Missing Directory\""));
        Assert.Contains(result.Warnings, x => x.Message.Contains("Steamfitter Scenario Template \"Missing Scenario Template\""));
        Assert.Contains(result.Warnings, x => x.Message.Contains("unpublished"));
        var stored = await Stored(imported.Id);
        Assert.Equal((null, null, null, false), (stored.ViewId, stored.DirectoryId, stored.ScenarioTemplateId, stored.IsPublished));
    }

    [Fact]
    public async Task Import_clears_a_view_without_a_default_team()
    {
        var exported = Exported("Moved");
        exported.ViewId = ArrangeView(defaultTeamId: null);

        var result = await ReadAsync<ImportEventTemplatesResult>(await Import(RootClient, Archive(exported)));

        var imported = Assert.Single(result.Imported);
        Assert.Contains(result.Warnings, x => x.Message.Contains("no default team"));
        Assert.Null((await Stored(imported.Id)).ViewId);
    }

    [Fact]
    public async Task Import_reports_an_archive_without_the_export_file()
    {
        using var archive = await new ArchiveService().ArchiveData("other", ArchiveType.zip,
            new Dictionary<string, string> { ["views.json"] = "[]" }, Ct);

        var result = await ReadAsync<ImportEventTemplatesResult>(
            await Import(RootClient, ToBytes(archive.Data), archive.Name));

        var failure = Assert.Single(result.Failures);
        Assert.Contains(EventTemplateArchiveService.ExportFileName, failure.Reason);
        await AssertNoTemplates();
    }

    [Fact]
    public async Task Import_refuses_a_file_that_is_not_an_archive()
    {
        var response = await Import(RootClient, Encoding.UTF8.GetBytes("not a zip"));

        var problem = await AssertJsonError(HttpStatusCode.BadRequest, response);
        Assert.Contains("could not be read as an archive", problem.Title);
        await AssertNoTemplates();
    }

    [Fact]
    public async Task Import_refuses_a_file_without_an_archive_extension()
    {
        var response = await Import(RootClient, Archive(Exported("Template")), "event_templates.json");

        var problem = await AssertJsonError(HttpStatusCode.BadRequest, response);
        Assert.Contains(".zip", problem.Title);
        await AssertNoTemplates();
    }

    [Fact]
    public async Task Import_refuses_a_request_without_an_archive()
    {
        using var form = new MultipartFormDataContent { { new StringContent("false"), nameof(ImportEventTemplatesForm.MatchReferencesByName) } };

        var problem = await AssertJsonError(HttpStatusCode.BadRequest, await RootClient.PostAsync(ImportUrl, form, Ct));

        Assert.Contains("archive file is required", problem.Title);
    }

    private const string ImportUrl = "api/eventTemplates/actions/import";

    private static string ExportUrl(params Guid[] ids) =>
        $"api/eventTemplates/actions/export?{string.Join("&", Array.ConvertAll(ids, id => $"ids={id}"))}";

    private static string ViewUrl(Guid id) => $"{PlayerApi}api/views/{id}";

    /// <summary>The url the Caster client asks for a directory at, without its related entities or file contents.</summary>
    private static string DirectoryUrl(Guid id) => $"{CasterApi}api/directories/{id}?IncludeRelated=false&IncludeFileContent=false";

    private static string ScenarioTemplateUrl(Guid id) => $"{SteamfitterApi}api/scenariotemplates/{id}";

    /// <summary>A view of Player's at a url of this test's own, with or without a default team.</summary>
    private Guid ArrangeView(Guid? defaultTeamId, string name = "View")
    {
        var id = Guid.NewGuid();
        Factory.OutboundHttp.RespondJson(ViewUrl(id), new { id, name, defaultTeamId });

        return id;
    }

    /// <summary>A directory of Caster's at a url of this test's own, visible (200) or refused.</summary>
    private Guid ArrangeDirectory(HttpStatusCode status, string name = "Directory")
    {
        var id = Guid.NewGuid();
        Factory.OutboundHttp.RespondJson(DirectoryUrl(id), new { id, name }, status);

        return id;
    }

    /// <summary>A scenario template of Steamfitter's at a url of this test's own, visible (200) or refused.</summary>
    private Guid ArrangeScenarioTemplate(HttpStatusCode status, string name = "Scenario Template")
    {
        var id = Guid.NewGuid();
        Factory.OutboundHttp.RespondJson(ScenarioTemplateUrl(id), new { id, name }, status);

        return id;
    }

    private static EventTemplateExport Exported(string name) => new() { Id = Guid.NewGuid(), Name = name, DurationHours = 1 };

    /// <summary>A zip holding <paramref name="templates"/> as <c>event_templates.json</c>, as an export writes it.</summary>
    private static byte[] Archive(params EventTemplateExport[] templates)
    {
        using var result = new ArchiveService().ArchiveData("event_templates", ArchiveType.zip,
            new Dictionary<string, string>
            {
                [EventTemplateArchiveService.ExportFileName] = JsonSerializer.Serialize(templates)
            }, TestContext.Current.CancellationToken).GetAwaiter().GetResult();

        return ToBytes(result.Data);
    }

    private Task<HttpResponseMessage> Import(HttpClient client, byte[] archive, string fileName = "event_templates.zip")
    {
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(archive), nameof(ImportEventTemplatesForm.Archive), fileName },
            { new StringContent("false"), nameof(ImportEventTemplatesForm.MatchReferencesByName) }
        };

        return client.PostAsync(ImportUrl, form, Ct);
    }

    private static async Task<EventTemplateExport[]> ReadExport(HttpResponseMessage response, string fileName)
    {
        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal(fileName, response.Content.Headers.ContentDisposition?.FileName);
        var entries = await new ArchiveService().ExtractArchive(
            new MemoryStream(await response.Content.ReadAsByteArrayAsync(Ct)), fileName, Ct);

        return JsonSerializer.Deserialize<EventTemplateExport[]>(entries[EventTemplateArchiveService.ExportFileName]);
    }

    private static byte[] ToBytes(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        return memory.ToArray();
    }

    private async Task<EventTemplateEntity> SeedTemplate(string name = "Template")
    {
        var template = TestData.EventTemplate(name);
        await Seed(template);

        return template;
    }

    private async Task DeleteTemplate(Guid id)
    {
        await using var context = NewContext();
        await context.EventTemplates.Where(x => x.Id == id).ExecuteDeleteAsync(Ct);
    }

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
