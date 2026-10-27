// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
/// <c>EventTemplateController</c>'s export and import over HTTP: a JSON file (an object for one template, an
/// array for several) written and read back as new templates, and the zip archives earlier exports wrote. A
/// reference to Player, Caster or Steamfitter is kept on import only if the importer can see it, and
/// otherwise cleared with a warning. Player, Caster and
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
    public async Task Export_of_one_template_by_a_member_holding_ViewEventTemplate_writes_it_as_a_json_object()
    {
        var template = await SeedTemplate("Range: Day 1");
        await SeedTemplate("Other");
        var actor = await Actor().OnEventTemplate(template.Id, [EventTemplatePermission.ViewEventTemplate]).SeedAsync();

        var response = await Client(actor).GetAsync(ExportUrl(template.Id), Ct);

        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Range_ Day 1.json", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        var exported = document.RootElement.Deserialize<EventTemplateExport>(ReadOptions);
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
    public async Task Export_of_every_template_by_a_caller_holding_ViewEventTemplates_writes_a_json_array()
    {
        await SeedTemplate("B");
        await SeedTemplate("A");
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewEventTemplates).SeedAsync();

        var response = await Client(actor).GetAsync("api/eventTemplates/actions/export", Ct);

        var exported = await ReadExport(response, EventTemplateArchiveService.ExportFileName);
        Assert.Equal(["A", "B"], exported.Select(x => x.Name));
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
    public async Task Export_of_several_named_templates_writes_them_as_a_json_array()
    {
        var first = await SeedTemplate("First");
        var second = await SeedTemplate("Second");
        await SeedTemplate("Left out");

        var exported = await ReadExport(await RootClient.GetAsync(ExportUrl(second.Id, first.Id), Ct), EventTemplateArchiveService.ExportFileName);

        Assert.Equal([first.Id, second.Id], exported.Select(x => x.Id));
    }

    [Fact]
    public async Task Export_of_a_template_that_does_not_exist_is_not_found()
    {
        var template = await SeedTemplate();
        var missing = Guid.NewGuid();

        var problem = await AssertJsonError(HttpStatusCode.NotFound, await RootClient.GetAsync(ExportUrl(template.Id, missing), Ct));

        Assert.Contains(missing.ToString(), problem.Title);
    }

    [Fact]
    public async Task Export_writes_each_reference_with_its_name()
    {
        var template = TestData.EventTemplate();
        template.ViewId = ArrangeView(Guid.NewGuid(), "Range View");
        template.DirectoryId = ArrangeDirectory(HttpStatusCode.OK, "Range Directory");
        template.ScenarioTemplateId = ArrangeScenarioTemplate(HttpStatusCode.OK, "Range Tasks");
        await Seed(template);

        var exported = Assert.Single(await ReadExport(await RootClient.GetAsync(ExportUrl(template.Id), Ct), "Template.json"));

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

        var exported = Assert.Single(await ReadExport(await RootClient.GetAsync(ExportUrl(template.Id), Ct), "Template.json"));

        Assert.Equal((template.ViewId, null), (exported.ViewId, exported.ViewName));
        Assert.Equal((template.DirectoryId, null), (exported.DirectoryId, exported.DirectoryName));
        Assert.Equal((template.ScenarioTemplateId, null), (exported.ScenarioTemplateId, exported.ScenarioTemplateName));
    }

    // POST api/eventTemplates/actions/import

    /// <summary>
    /// Exported and imported back into the same environment, a template is a new copy: a new id, and the
    /// importer's name appended because the original still holds the name.
    /// </summary>
    [Fact]
    public async Task Import_of_an_export_creates_a_copy_with_a_new_id_its_fields_and_visible_references()
    {
        var template = TestData.EventTemplate(published: true, durationHours: 3);
        template.Description = "Description";
        template.UseDynamicHost = true;
        template.ViewId = ArrangeView(Guid.NewGuid());
        template.DirectoryId = ArrangeDirectory(HttpStatusCode.OK);
        template.ScenarioTemplateId = ArrangeScenarioTemplate(HttpStatusCode.OK);
        await Seed(template);
        var export = await RootClient.GetAsync(ExportUrl(template.Id), Ct);
        await AssertStatus(HttpStatusCode.OK, export);

        var result = await ReadAsync<ImportEventTemplatesResult>(
            await Import(RootClient, await export.Content.ReadAsByteArrayAsync(Ct), "Template.json"));

        Assert.Empty(result.Failures);
        var imported = Assert.Single(result.Imported);
        Assert.NotEqual(template.Id, imported.Id);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal((imported.Id, "Template - Root"), (warning.Id, warning.Name));
        Assert.Equal("Renamed to \"Template - Root\" because an Event Template named \"Template\" already exists.", warning.Message);
        var stored = await Stored(imported.Id);
        Assert.Equal(("Template - Root", template.Description, 3, true, true),
            (stored.Name, stored.Description, stored.DurationHours, stored.UseDynamicHost, stored.IsPublished));
        Assert.Equal((template.ViewId, template.DirectoryId, template.ScenarioTemplateId),
            (stored.ViewId, stored.DirectoryId, stored.ScenarioTemplateId));
        Assert.Equal(template.Name, (await Stored(template.Id)).Name);
    }

    /// <summary>A template is imported under its own name where no template holds it, so nothing is said about it.</summary>
    [Fact]
    public async Task Import_of_a_json_array_creates_each_template_under_its_own_name()
    {
        var result = await ReadAsync<ImportEventTemplatesResult>(await Import(RootClient, Json(Exported("First"), Exported("Second"))));

        Assert.Empty(result.Failures);
        Assert.Empty(result.Warnings);
        Assert.Equal(["First", "Second"], result.Imported.Select(x => x.Name));
        Assert.Equal(2, await ReadBack(db => db.EventTemplates.CountAsync(Ct)));
    }

    /// <summary>
    /// Earlier exports wrote the array as <c>event_templates.json</c> in a zip archive, with PascalCase names.
    /// The file is told apart by its content, so the archive's name does not matter.
    /// </summary>
    [Theory]
    [InlineData("event_templates.zip")]
    [InlineData("event_templates.json")]
    public async Task Import_reads_a_zip_archive_from_an_earlier_export(string fileName)
    {
        var archive = Zip(new()
        {
            [EventTemplateArchiveService.LegacyExportFileName] = JsonSerializer.Serialize(new[] { Exported("Zipped") })
        });

        var result = await ReadAsync<ImportEventTemplatesResult>(await Import(RootClient, archive, fileName));

        Assert.Equal("Zipped", Assert.Single(result.Imported).Name);
    }

    [Fact]
    public async Task Import_reads_a_json_file_that_starts_with_a_byte_order_mark()
    {
        byte[] file = [0xEF, 0xBB, 0xBF, .. Json(Exported("Saved by an editor"))];

        var result = await ReadAsync<ImportEventTemplatesResult>(await Import(RootClient, file));

        Assert.Equal("Saved by an editor", Assert.Single(result.Imported).Name);
    }

    [Fact]
    public async Task Import_by_a_caller_holding_CreateEventTemplates_creates_the_template()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.CreateEventTemplates).SeedAsync();

        var result = await ReadAsync<ImportEventTemplatesResult>(await Import(Client(actor), Json(Exported("Imported"))));

        var imported = Assert.Single(result.Imported);
        Assert.Equal("Imported", (await Stored(imported.Id)).Name);
    }

    [Fact]
    public async Task Import_is_forbidden_for_a_caller_holding_only_EditEventTemplates()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.EditEventTemplates).SeedAsync();

        await AssertJsonError(HttpStatusCode.Forbidden, await Import(Client(actor), Json(Exported("Denied"))));

        await AssertNoTemplates();
    }

    /// <summary>The id in the file belongs to the environment it was exported from, so a template holding it is left alone.</summary>
    [Fact]
    public async Task Import_never_reuses_the_id_in_the_file()
    {
        var template = await SeedTemplate("Original");
        var exported = Exported("Changed");
        exported.Id = template.Id;

        var result = await ReadAsync<ImportEventTemplatesResult>(await Import(RootClient, Json(exported)));

        Assert.Empty(result.Failures);
        var imported = Assert.Single(result.Imported);
        Assert.NotEqual(template.Id, imported.Id);
        Assert.Equal("Original", (await Stored(template.Id)).Name);
        Assert.Equal("Changed", (await Stored(imported.Id)).Name);
    }

    /// <summary>The second of two same-named templates in one file collides with the first, imported just before it.</summary>
    [Fact]
    public async Task Import_renames_a_template_whose_name_an_earlier_one_in_the_file_took()
    {
        var importer = await Actor().WithName("Ada Importer").WithSystemPermissions(SystemPermission.CreateEventTemplates).SeedAsync();

        var result = await ReadAsync<ImportEventTemplatesResult>(await Import(Client(importer), Json(Exported("Twin"), Exported("Twin"))));

        Assert.Equal(["Twin", "Twin - Ada Importer"], result.Imported.Select(x => x.Name));
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(result.Imported[1].Id, warning.Id);
        Assert.Contains("Renamed to \"Twin - Ada Importer\"", warning.Message);
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

        var result = await ReadAsync<ImportEventTemplatesResult>(await Import(RootClient, Json(exported)));

        Assert.Empty(result.Failures);
        var imported = Assert.Single(result.Imported);
        Assert.Contains(result.Warnings, x => x.Message.Contains($"Player View \"Missing View\" ({exported.ViewId})"));
        Assert.Contains(result.Warnings, x => x.Message.Contains($"Caster Directory \"Missing Directory\" ({exported.DirectoryId})"));
        Assert.Contains(result.Warnings, x => x.Message.Contains("Steamfitter Scenario Template \"Missing Scenario Template\""));
        Assert.Contains(result.Warnings, x => x.Message.Contains("unpublished"));
        Assert.All(result.Warnings, x => Assert.Equal(imported.Id, x.Id));
        var stored = await Stored(imported.Id);
        Assert.Equal((null, null, null, false), (stored.ViewId, stored.DirectoryId, stored.ScenarioTemplateId, stored.IsPublished));
    }

    [Fact]
    public async Task Import_clears_a_view_without_a_default_team()
    {
        var exported = Exported("Moved");
        exported.ViewId = ArrangeView(defaultTeamId: null);

        var result = await ReadAsync<ImportEventTemplatesResult>(await Import(RootClient, Json(exported)));

        var imported = Assert.Single(result.Imported);
        Assert.Contains(result.Warnings, x => x.Message.Contains("no default team"));
        Assert.Null((await Stored(imported.Id)).ViewId);
    }

    /// <summary>Each item is reported on its own, so one that is not a template does not stop the others.</summary>
    [Fact]
    public async Task Import_reports_an_item_that_is_not_a_template_and_imports_the_rest()
    {
        var file = Encoding.UTF8.GetBytes("""[{"name":"Good","durationHours":1}, 42, null, {"name":""}, {"name":"Bad","durationHours":"x"}]""");

        var result = await ReadAsync<ImportEventTemplatesResult>(await Import(RootClient, file));

        Assert.Equal("Good", Assert.Single(result.Imported).Name);
        Assert.Equal(4, result.Failures.Length);
        Assert.Equal("Item 2 is not an Event Template", result.Failures[0].Reason);
        Assert.Equal("Item 3 is not an Event Template", result.Failures[1].Reason);
        Assert.Equal("An Event Template must have a Name", result.Failures[2].Reason);
        Assert.StartsWith("Item 5 could not be read as an Event Template", result.Failures[3].Reason);
    }

    [Fact]
    public async Task Import_refuses_a_zip_archive_without_a_json_file()
    {
        var archive = Zip(new() { ["views.txt"] = "[]" });

        var problem = await AssertJsonError(HttpStatusCode.BadRequest, await Import(RootClient, archive, "export.zip"));

        Assert.Equal("The zip archive holds no .json file.", problem.Title);
        await AssertNoTemplates();
    }

    [Fact]
    public async Task Import_refuses_a_file_that_is_neither_json_nor_a_zip_archive()
    {
        var problem = await AssertJsonError(HttpStatusCode.BadRequest, await Import(RootClient, Encoding.UTF8.GetBytes("not json")));

        Assert.StartsWith("The file is not an Event Template export", problem.Title);
        await AssertNoTemplates();
    }

    /// <summary>The bound is on what is extracted, so a small archive that inflates past it is refused too.</summary>
    [Fact]
    public async Task Import_refuses_a_zip_archive_that_extracts_to_more_than_the_limit()
    {
        var archive = Zip(new() { ["event_templates.json"] = new string(' ', (int)ArchiveService.MaxBytes + 1) });

        var problem = await AssertJsonError(HttpStatusCode.BadRequest, await Import(RootClient, archive, "event_templates.zip"));

        Assert.StartsWith("The file could not be read", problem.Title);
        await AssertNoTemplates();
    }

    [Fact]
    public async Task Import_refuses_a_request_without_a_file()
    {
        using var form = new MultipartFormDataContent { { new StringContent("false"), nameof(ImportEventTemplatesForm.MatchReferencesByName) } };

        var problem = await AssertJsonError(HttpStatusCode.BadRequest, await RootClient.PostAsync(ImportUrl, form, Ct));

        Assert.Equal("A .json or .zip file is required", problem.Title);
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

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static EventTemplateExport Exported(string name) => new() { Id = Guid.NewGuid(), Name = name, DurationHours = 1 };

    /// <summary>One template as a JSON object, several as an array, as an export writes them.</summary>
    private static byte[] Json(params EventTemplateExport[] templates) => templates.Length == 1
        ? JsonSerializer.SerializeToUtf8Bytes(templates[0], AlloyJson)
        : JsonSerializer.SerializeToUtf8Bytes(templates, AlloyJson);

    private static byte[] Zip(Dictionary<string, string> entries)
    {
        using var stream = new MemoryStream();

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private Task<HttpResponseMessage> Import(HttpClient client, byte[] file, string fileName = "event-templates.json")
    {
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(file), nameof(ImportEventTemplatesForm.File), fileName },
            { new StringContent("false"), nameof(ImportEventTemplatesForm.MatchReferencesByName) }
        };

        return client.PostAsync(ImportUrl, form, Ct);
    }

    /// <summary>The templates in an export's response: the one in an object, or every one in an array.</summary>
    private static async Task<EventTemplateExport[]> ReadExport(HttpResponseMessage response, string fileName)
    {
        await AssertStatus(HttpStatusCode.OK, response);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(fileName, response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(Ct));

        return document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.Deserialize<EventTemplateExport[]>(ReadOptions)
            : [document.RootElement.Deserialize<EventTemplateExport>(ReadOptions)];
    }

    private async Task<EventTemplateEntity> SeedTemplate(string name = "Template")
    {
        var template = TestData.EventTemplate(name);
        await Seed(template);

        return template;
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
