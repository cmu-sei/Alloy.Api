// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.Infrastructure.Extensions;
using Alloy.Api.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alloy.Api.Services
{
    /// <summary>
    /// Exports Event Templates to a JSON file and imports them back as new templates: a JSON object for one
    /// template, a JSON array for several, with a per-template list of failures and warnings. An import also
    /// reads a zip archive holding the JSON file, as earlier exports wrote it.
    /// </summary>
    public interface IEventTemplateArchiveService
    {
        Task<ExportFile> ExportAsync(Guid[] ids, CancellationToken ct);
        Task<ImportEventTemplatesResult> ImportAsync(Stream file, bool matchReferencesByName, CancellationToken ct);
    }

    public class EventTemplateArchiveService : IEventTemplateArchiveService
    {
        /// <summary>The file name of an export of several Event Templates.</summary>
        public const string ExportFileName = "event-templates.json";

        /// <summary>The file earlier exports wrapped in a zip archive.</summary>
        public const string LegacyExportFileName = "event_templates.json";

        public const string JsonContentType = "application/json";

        private static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            // Names and descriptions are written as typed, not as \u escapes; the file is a download, not HTML.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly AlloyContext _context;
        private readonly ClaimsPrincipal _user;
        private readonly IEventTemplateService _eventTemplateService;
        private readonly IPlayerService _playerService;
        private readonly ICasterService _casterService;
        private readonly ISteamfitterService _steamfitterService;
        private readonly IArchiveService _archiveService;
        private readonly ILogger<EventTemplateArchiveService> _logger;

        public EventTemplateArchiveService(
            AlloyContext context,
            IPrincipal user,
            IEventTemplateService eventTemplateService,
            IPlayerService playerService,
            ICasterService casterService,
            ISteamfitterService steamfitterService,
            IArchiveService archiveService,
            ILogger<EventTemplateArchiveService> logger)
        {
            _context = context;
            _user = user as ClaimsPrincipal;
            _eventTemplateService = eventTemplateService;
            _playerService = playerService;
            _casterService = casterService;
            _steamfitterService = steamfitterService;
            _archiveService = archiveService;
            _logger = logger;
        }

        #region Export

        public async Task<ExportFile> ExportAsync(Guid[] ids, CancellationToken ct)
        {
            ids = (ids ?? []).Distinct().ToArray();
            var query = _context.EventTemplates.AsNoTracking();

            if (ids.Length > 0)
            {
                query = query.Where(x => ids.Contains(x.Id));
            }

            var entities = await query.OrderBy(x => x.Name).ToListAsync(ct);
            var missing = ids.Except(entities.Select(x => x.Id)).ToList();

            if (missing.Count > 0)
                throw new EntityNotFoundException<EventTemplate>($"Event Template {missing[0]} was not found.");

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

            // One requested template is written as an object named after it, anything else as an array.
            return ids.Length == 1
                ? new ExportFile(SingleFileName(export[0].Name), JsonContentType, JsonSerializer.SerializeToUtf8Bytes(export[0], WriteOptions))
                : new ExportFile(ExportFileName, JsonContentType, JsonSerializer.SerializeToUtf8Bytes(export, WriteOptions));
        }

        // The characters Windows refuses in a file name, a superset of what the server's own OS refuses, since the
        // file is saved on the user's machine.
        private static readonly char[] InvalidFileNameChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

        private static string SingleFileName(string templateName)
        {
            var name = new string(templateName.Select(c => InvalidFileNameChars.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim(' ', '.');

            return string.IsNullOrEmpty(name) ? "event-template.json" : $"{name}.json";
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

        public async Task<ImportEventTemplatesResult> ImportAsync(Stream file, bool matchReferencesByName, CancellationToken ct)
        {
            var items = await ReadItemsAsync(file, ct);

            var imported = new List<EventTemplate>();
            var failures = new List<ImportEventTemplateFailure>();
            var warnings = new List<ImportEventTemplateWarning>();

            var userName = await GetUserNameAsync(ct);
            var names = (await _context.EventTemplates.Select(x => x.Name).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
            var resolver = new ReferenceResolver(_playerService, _casterService, _steamfitterService, matchReferencesByName, ct);

            // One transaction, so a request that is cancelled part way (the user closed the dialog) imports nothing
            // rather than the templates before it. EF Core puts each SaveChanges in it under a savepoint, so one
            // template that cannot be saved still fails on its own. Entity events are published on commit.
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);

            for (var i = 0; i < items.Count; i++)
            {
                EventTemplateExport template;

                try
                {
                    template = items[i].ValueKind == JsonValueKind.Object
                        ? items[i].Deserialize<EventTemplateExport>(ReadOptions)
                        : null;
                }
                catch (JsonException ex)
                {
                    failures.Add(new(null, null, $"Item {i + 1} could not be read as an Event Template: {ex.Message}"));
                    continue;
                }

                if (template == null)
                {
                    failures.Add(new(null, null, $"Item {i + 1} is not an Event Template"));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(template.Name))
                {
                    failures.Add(new(template.Id, template.Name, "An Event Template must have a Name"));
                    continue;
                }

                var messages = new List<string>();
                var name = template.Name;

                if (names.Contains(name))
                {
                    name = $"{template.Name} - {userName}";
                    messages.Add($"Renamed to \"{name}\" because an Event Template named \"{template.Name}\" already exists.");
                }

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

                // The id in the file is not reused: an import always creates a new template.
                var eventTemplate = new EventTemplate
                {
                    Name = name,
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
                    names.Add(created.Name);
                    warnings.AddRange(messages.Select(m => new ImportEventTemplateWarning(created.Id, created.Name, m)));
                }
                catch (ValidationException ex)
                {
                    failures.Add(new(template.Id, template.Name, ex.Message));
                }
                catch (DbUpdateException ex)
                {
                    _logger.LogWarning(ex, "Could not import Event Template {Name}.", template.Name);
                    _context.ChangeTracker.Clear();
                    failures.Add(new(template.Id, template.Name, "The Event Template could not be saved"));
                }
            }

            await transaction.CommitAsync(ct);

            return new ImportEventTemplatesResult(imported.ToArray(), failures.ToArray(), warnings.ToArray());
        }

        /// <summary>
        /// The templates in an uploaded file: a JSON object or array, or a zip archive holding one, told apart by
        /// content rather than by the file's name.
        /// </summary>
        private async Task<List<JsonElement>> ReadItemsAsync(Stream file, CancellationToken ct)
        {
            byte[] json;

            try
            {
                json = await ArchiveService.ReadBounded(file, ArchiveService.MaxBytes, ct);

                if (ArchiveService.IsZip(json))
                {
                    json = FindJson(await _archiveService.ExtractZip(new MemoryStream(json), ct));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                throw new ValidationException($"The file could not be read: {ex.Message}", ex);
            }

            // A file saved by an editor may start with a UTF-8 byte order mark, which the JSON reader rejects.
            var content = json.AsMemory();

            if (content.Span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
                content = content[3..];

            JsonElement root;

            try
            {
                using var document = JsonDocument.Parse(content);
                root = document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new ValidationException($"The file is not an Event Template export (a .json file, or a .zip archive holding one): {ex.Message}", ex);
            }

            return root.ValueKind switch
            {
                JsonValueKind.Object => [root],
                JsonValueKind.Array => root.EnumerateArray().ToList(),
                _ => throw new ValidationException("The file is not an Event Template export: expected a JSON object or array.")
            };
        }

        /// <summary>The JSON file in a zip archive: the one earlier exports wrote, otherwise the only .json entry.</summary>
        private static byte[] FindJson(Dictionary<string, byte[]> entries)
        {
            var legacy = entries.Where(x => x.Key == LegacyExportFileName || x.Key.EndsWith("/" + LegacyExportFileName, StringComparison.Ordinal)).ToList();

            if (legacy.Count > 0)
                return legacy[0].Value;

            var json = entries.Where(x => x.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToList();

            return json.Count == 1
                ? json[0].Value
                : throw new ValidationException(json.Count == 0
                    ? "The zip archive holds no .json file."
                    : "The zip archive holds more than one .json file, so it is not clear which to import.");
        }

        private async Task<string> GetUserNameAsync(CancellationToken ct)
        {
            var userId = _user.GetId();
            var name = await _context.Users.Where(x => x.Id == userId).Select(x => x.Name).SingleOrDefaultAsync(ct);

            return string.IsNullOrWhiteSpace(name) ? _user.FindFirst("name")?.Value ?? userId.ToString() : name;
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
                    async () => (_views ??= (await playerService.GetVisibleViewsAsync(ct)).ToList())
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
