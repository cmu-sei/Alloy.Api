// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Services;
using Alloy.Api.Tests.Support;
using Alloy.Api.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Alloy.Api.Tests.Services;

/// <summary>
/// <see cref="EventTemplateArchiveService"/> matching a reference by name. A reference whose id does not
/// resolve is looked for in the sibling's list of what the caller can see (Player's views of the user,
/// Caster's directories, Steamfitter's scenario templates), urls that are the same for every test and so
/// cannot be arranged on the run-wide host; each test here scripts its own. The export, the gates and the
/// other import rules are tested over HTTP in <c>Controllers/EventTemplateArchiveTests</c>.
/// </summary>
public class EventTemplateArchiveServiceTests(DatabaseFixture fixture) : ServiceTestBase(fixture)
{
    [Fact]
    public async Task Import_matching_by_name_replaces_each_missing_reference_with_the_one_visible_match()
    {
        var principal = new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.CreateEventTemplates);
        var host = HostFor(principal.Build());
        var (viewId, directoryId, scenarioTemplateId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        host.SiblingApis.Handle = message => Task.FromResult(message.RequestUri.AbsolutePath switch
        {
            var path when path == $"/api/views/{viewId}" => FakeSiblingApis.Json(View(viewId, "Range")),
            var path when path == $"/api/users/{principal.UserId}/views" => FakeSiblingApis.Json(new[] { View(viewId, "Range") }),
            var path when path == $"/api/directories/{directoryId}" => FakeSiblingApis.Json(new { id = directoryId, name = "Network" }),
            "/api/directories" => FakeSiblingApis.Json(new[] { new { id = directoryId, name = "Network" } }),
            var path when path == $"/api/scenariotemplates/{scenarioTemplateId}" => FakeSiblingApis.Json(new { id = scenarioTemplateId, name = "Tasks" }),
            "/api/scenariotemplates" => FakeSiblingApis.Json(new[] { new { id = scenarioTemplateId, name = "Tasks" } }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var exported = new EventTemplateExport
        {
            Id = Guid.NewGuid(), Name = "Moved", DurationHours = 1,
            ViewId = Guid.NewGuid(), ViewName = "Range",
            DirectoryId = Guid.NewGuid(), DirectoryName = "Network",
            ScenarioTemplateId = Guid.NewGuid(), ScenarioTemplateName = "Tasks"
        };

        var result = await Import(host, exported);

        Assert.Empty(result.Failures);
        Assert.Equal(3, result.Warnings.Length);
        Assert.All(result.Warnings, x => Assert.Contains("Matched by name", x.Message));
        var imported = Assert.Single(result.Imported);
        var stored = await ReadBack(db => db.EventTemplates.AsNoTracking().SingleAsync(x => x.Id == imported.Id, Ct));
        Assert.Equal((viewId, directoryId, scenarioTemplateId), (stored.ViewId, stored.DirectoryId, stored.ScenarioTemplateId));
    }

    [Fact]
    public async Task Import_matching_by_name_clears_a_reference_whose_name_is_ambiguous()
    {
        var host = HostFor(new ClaimsPrincipalBuilder().WithSystemPermissions(SystemPermission.CreateEventTemplates).Build());
        host.SiblingApis.Handle = message => Task.FromResult(message.RequestUri.AbsolutePath == "/api/directories"
            ? FakeSiblingApis.Json(new[] { new { id = Guid.NewGuid(), name = "Network" }, new { id = Guid.NewGuid(), name = "Network" } })
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var exported = new EventTemplateExport
        {
            Id = Guid.NewGuid(), Name = "Moved", DurationHours = 1, DirectoryId = Guid.NewGuid(), DirectoryName = "Network"
        };

        var result = await Import(host, exported);

        var warning = Assert.Single(result.Warnings);
        Assert.Contains("more than one", warning.Message);
        var imported = Assert.Single(result.Imported);
        Assert.Null((await ReadBack(db => db.EventTemplates.AsNoTracking().SingleAsync(x => x.Id == imported.Id, Ct))).DirectoryId);
    }

    private static object View(Guid id, string name) => new { id, name, defaultTeamId = Guid.NewGuid() };

    private static async Task<ImportEventTemplatesResult> Import(ApiTestHost host, params EventTemplateExport[] templates)
    {
        var archiveService = host.Resolve<IArchiveService>();
        using var archive = await archiveService.ArchiveData("event_templates", ArchiveType.zip,
            new Dictionary<string, string> { [EventTemplateArchiveService.ExportFileName] = JsonSerializer.Serialize(templates) }, Ct);
        using var scope = host.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IEventTemplateArchiveService>();

        return await service.ImportAsync(archive.Data, archive.Name, matchReferencesByName: true, Ct);
    }
}
