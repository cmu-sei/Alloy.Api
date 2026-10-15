// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alloy.Api.Services
{
    /// <summary>
    /// Exports Event Templates to an archive and imports them back, following Player's View export:
    /// a zip or tar.gz holding one JSON file, ids preserved, and a per-template list of failures.
    /// </summary>
    public interface IEventTemplateArchiveService
    {
        Task<ArchiveResult> ExportAsync(Guid[] ids, ArchiveType archiveType, CancellationToken ct);
        Task<ImportEventTemplatesResult> ImportAsync(Stream archive, string archiveFileName, bool matchReferencesByName, CancellationToken ct);
    }

    public class EventTemplateArchiveService : IEventTemplateArchiveService
    {
        public const string ExportFileName = "event_templates.json";
        public const string ArchiveName = "event_templates";

        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
        private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly AlloyContext _context;
        private readonly IEventTemplateService _eventTemplateService;
        private readonly IPlayerService _playerService;
        private readonly ICasterService _casterService;
        private readonly ISteamfitterService _steamfitterService;
        private readonly IArchiveService _archiveService;
        private readonly ILogger<EventTemplateArchiveService> _logger;

        public EventTemplateArchiveService(
            AlloyContext context,
            IEventTemplateService eventTemplateService,
            IPlayerService playerService,
            ICasterService casterService,
            ISteamfitterService steamfitterService,
            IArchiveService archiveService,
            ILogger<EventTemplateArchiveService> logger)
        {
            _context = context;
            _eventTemplateService = eventTemplateService;
            _playerService = playerService;
            _casterService = casterService;
            _steamfitterService = steamfitterService;
            _archiveService = archiveService;
            _logger = logger;
        }

        #region Export

        public async Task<ArchiveResult> ExportAsync(Guid[] ids, ArchiveType archiveType, CancellationToken ct)
        {
            var query = _context.EventTemplates.AsNoTracking();

            if (ids != null && ids.Length > 0)
            {
                query = query.Where(x => ids.Contains(x.Id));
            }

            var entities = await query.OrderBy(x => x.Name).ToListAsync(ct);

            // Names are a convenience for the importer, so a reference that cannot be read now is still exported by id.
            var viewNames = await GetNamesAsync(entities.Select(x => x.ViewId),
                async id => (await _playerService.GetViewAsync(id, ct))?.Name, "Player View");
            var directoryNames = await GetNamesAsync(entities.Select(x => x.DirectoryId),
                async id => (await _casterService.GetDirectoryAsync(id, ct))?.Name, "Caster Directory");
            var scenarioTemplateNames = await GetNamesAsync(entities.Select(x => x.ScenarioTemplateId),
                async id => (await _steamfitterService.GetScenarioTemplateAsync(id, ct))?.Name, "Steamfitter Scenario Template");

            var export = entities.Select(x => new EventTemplateExport
            {
                Id = x.Id,
                Name = x.Name,
                Description = x.Description,
                DurationHours = x.DurationHours,
                UseDynamicHost = x.UseDynamicHost,
                IsPublished = x.IsPublished,
                ViewId = x.ViewId,
                ViewName = x.ViewId.HasValue ? viewNames.GetValueOrDefault(x.ViewId.Value) : null,
                DirectoryId = x.DirectoryId,
                DirectoryName = x.DirectoryId.HasValue ? directoryNames.GetValueOrDefault(x.DirectoryId.Value) : null,
                ScenarioTemplateId = x.ScenarioTemplateId,
                ScenarioTemplateName = x.ScenarioTemplateId.HasValue ? scenarioTemplateNames.GetValueOrDefault(x.ScenarioTemplateId.Value) : null,
            }).ToArray();

            var data = new Dictionary<string, string>
            {
                [ExportFileName] = JsonSerializer.Serialize(export, WriteOptions)
            };

            return await _archiveService.ArchiveData(ArchiveName, archiveType, data, ct);
        }

        private async Task<Dictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid?> ids, Func<Guid, Task<string>> getName, string description)
        {
            var names = new Dictionary<Guid, string>();

            foreach (var id in ids.Where(x => x.HasValue).Select(x => x.Value).Distinct())
            {
                try
                {
                    names[id] = await getName(id);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogInformation(ex, "Could not read the name of {Description} {Id} for an Event Template export.", description, id);
                }
            }

            return names;
        }

        #endregion

        #region Import

        public async Task<ImportEventTemplatesResult> ImportAsync(Stream archive, string archiveFileName, bool matchReferencesByName, CancellationToken ct)
        {
            Dictionary<string, byte[]> entries;

            try
            {
                entries = await _archiveService.ExtractArchive(archive, archiveFileName, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IOException)
            {
                throw new ValidationException($"The file could not be read as an archive: {ex.Message}", ex);
            }

            var templates = Deserialize(entries);

            if (templates == null)
            {
                return new ImportEventTemplatesResult([],
                    [new(null, ExportFileName, $"Missing or invalid {ExportFileName} in archive")], []);
            }

            var imported = new List<EventTemplate>();
            var failures = new List<ImportEventTemplateFailure>();
            var warnings = new List<ImportEventTemplateWarning>();

            var incomingIds = templates.Select(x => x.Id).Where(x => x != Guid.Empty).ToList();
            var takenIds = (await _context.EventTemplates
                .Where(x => incomingIds.Contains(x.Id))
                .Select(x => x.Id)
                .ToListAsync(ct)).ToHashSet();

            var resolver = new ReferenceResolver(_playerService, _casterService, _steamfitterService, matchReferencesByName, ct);

            foreach (var template in templates)
            {
                if (string.IsNullOrWhiteSpace(template.Name))
                {
                    failures.Add(new(template.Id, template.Name, "An Event Template must have a Name"));
                    continue;
                }

                if (template.Id != Guid.Empty && !takenIds.Add(template.Id))
                {
                    failures.Add(new(template.Id, template.Name, "An Event Template with this Id already exists",
                        ImportEventTemplateFailureType.EventTemplateExists));
                    continue;
                }

                var messages = new List<string>();
                var viewId = await resolver.ResolveViewAsync(template, messages);
                var directoryId = await resolver.ResolveDirectoryAsync(template, messages);
                var scenarioTemplateId = await resolver.ResolveScenarioTemplateAsync(template, messages);

                var referenceCleared =
                    (template.ViewId.HasValue && !viewId.HasValue) ||
                    (template.DirectoryId.HasValue && !directoryId.HasValue) ||
                    (template.ScenarioTemplateId.HasValue && !scenarioTemplateId.HasValue);

                var isPublished = template.IsPublished;

                if (isPublished && referenceCleared)
                {
                    // A published template is offered to every user to launch, and it cannot launch correctly
                    // without what it used to reference.
                    isPublished = false;
                    messages.Add("Imported unpublished because a reference was cleared. Publish it after choosing the missing reference.");
                }

                var eventTemplate = new EventTemplate
                {
                    Id = template.Id,
                    Name = template.Name,
                    Description = template.Description,
                    DurationHours = template.DurationHours,
                    UseDynamicHost = template.UseDynamicHost,
                    IsPublished = isPublished,
                    ViewId = viewId,
                    DirectoryId = directoryId,
                    ScenarioTemplateId = scenarioTemplateId,
                };

                try
                {
                    // Create applies the same rules as any other new template, including that the caller can see
                    // every reference, so an import cannot attach resources the caller could not choose by hand.
                    var created = await _eventTemplateService.CreateAsync(eventTemplate, ct);
                    imported.Add(created);
                    warnings.AddRange(messages.Select(m => new ImportEventTemplateWarning(created.Id, created.Name, m)));
                }
                catch (ValidationException ex)
                {
                    failures.Add(new(template.Id, template.Name, ex.Message));
                }
                catch (DbUpdateException ex)
                {
                    _logger.LogWarning(ex, "Could not import Event Template {Id}.", template.Id);
                    _context.ChangeTracker.Clear();
                    failures.Add(new(template.Id, template.Name, "The Event Template could not be saved"));
                }
            }

            return new ImportEventTemplatesResult(imported.ToArray(), failures.ToArray(), warnings.ToArray());
        }

        private static EventTemplateExport[] Deserialize(Dictionary<string, byte[]> entries)
        {
            var json = entries.TryGetValue(ExportFileName, out var bytes)
                ? bytes
                : entries.Where(x => x.Key.EndsWith("/" + ExportFileName, StringComparison.Ordinal))
                    .Select(x => x.Value)
                    .FirstOrDefault();

            if (json == null)
                return null;

            try
            {
                return JsonSerializer.Deserialize<EventTemplateExport[]>(json, ReadOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Resolves an exported reference to another application against what the importing user can see there.
        /// A reference is kept if its id resolves, optionally replaced by a single same-named match, and
        /// otherwise cleared with a warning so the template still imports and can be fixed in the editor.
        /// </summary>
        private sealed class ReferenceResolver(
            IPlayerService playerService,
            ICasterService casterService,
            ISteamfitterService steamfitterService,
            bool matchByName,
            CancellationToken ct)
        {
            private List<Player.Api.Client.View> _views;
            private List<Caster.Api.Client.Directory> _directories;
            private List<Steamfitter.Api.Client.ScenarioTemplate> _scenarioTemplates;

            public Task<Guid?> ResolveViewAsync(EventTemplateExport template, List<string> messages) =>
                ResolveAsync("Player View", template.ViewId, template.ViewName, messages,
                    async id =>
                    {
                        var view = await playerService.GetViewAsync(id, ct);
                        return view.DefaultTeamId.HasValue ? null : "has no default team";
                    },
                    async () => (_views ??= (await playerService.GetViewsAsync(ct)).ToList())
                        .Select(x => (x.Id, x.Name, x.DefaultTeamId.HasValue ? null : "has no default team")));

            public Task<Guid?> ResolveDirectoryAsync(EventTemplateExport template, List<string> messages) =>
                ResolveAsync("Caster Directory", template.DirectoryId, template.DirectoryName, messages,
                    async id =>
                    {
                        await casterService.GetDirectoryAsync(id, ct);
                        return null;
                    },
                    async () => (_directories ??= (await casterService.GetDirectoriesAsync(ct)).ToList())
                        .Select(x => (x.Id, x.Name, (string)null)));

            public Task<Guid?> ResolveScenarioTemplateAsync(EventTemplateExport template, List<string> messages) =>
                ResolveAsync("Steamfitter Scenario Template", template.ScenarioTemplateId, template.ScenarioTemplateName, messages,
                    async id =>
                    {
                        await steamfitterService.GetScenarioTemplateAsync(id, ct);
                        return null;
                    },
                    async () => (_scenarioTemplates ??= (await steamfitterService.GetScenarioTemplatesAsync(ct)).ToList())
                        .Select(x => (x.Id, x.Name, (string)null)));

            /// <param name="checkById">Returns null if the id is usable, otherwise why it is not.</param>
            /// <param name="list">Candidates visible to the caller, with why each is unusable (or null).</param>
            private async Task<Guid?> ResolveAsync(
                string description,
                Guid? id,
                string name,
                List<string> messages,
                Func<Guid, Task<string>> checkById,
                Func<Task<IEnumerable<(Guid Id, string Name, string Problem)>>> list)
            {
                if (!id.HasValue)
                    return null;

                var label = string.IsNullOrEmpty(name) ? $"{description} {id}" : $"{description} \"{name}\" ({id})";
                string problem;

                try
                {
                    problem = await checkById(id.Value);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    problem = "was not found or is not visible to you";
                }

                if (problem == null)
                    return id;

                if (matchByName && !string.IsNullOrEmpty(name))
                {
                    try
                    {
                        var matches = (await list()).Where(x => x.Name == name).ToList();

                        if (matches.Count == 1 && matches[0].Problem == null)
                        {
                            messages.Add($"{label} {problem}. Matched by name to {description} {matches[0].Id}.");
                            return matches[0].Id;
                        }

                        if (matches.Count > 1)
                        {
                            problem += $", and more than one {description} is named \"{name}\"";
                        }
                        else if (matches.Count == 1)
                        {
                            problem += $", and the {description} with that name {matches[0].Problem}";
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        problem += $", and the list of {description}s could not be read to match by name";
                    }
                }

                messages.Add($"{label} {problem}, so the reference was cleared.");
                return null;
            }
        }

        #endregion
    }
}
