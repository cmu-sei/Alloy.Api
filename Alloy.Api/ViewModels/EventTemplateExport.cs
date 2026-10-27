// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.AspNetCore.Http;

namespace Alloy.Api.ViewModels
{
    /// <summary>
    /// One Event Template as written to an export file: a JSON object for one template, a JSON array for several.
    /// </summary>
    /// <remarks>
    /// The Player View, Caster Directory and Steamfitter Scenario Template live in other applications,
    /// so only their ids travel. Each id is paired with the name it had at export time, so an import into
    /// another environment can match it by name and can say which reference it could not resolve.
    /// Memberships are not exported: users and groups are specific to the environment.
    /// The Id is the template's id where it was exported; an import always creates a template with a new id.
    /// </remarks>
    public class EventTemplateExport
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int DurationHours { get; set; }
        public bool UseDynamicHost { get; set; }
        public bool IsPublished { get; set; }

        public Guid? ViewId { get; set; }
        public string ViewName { get; set; }

        public Guid? DirectoryId { get; set; }
        public string DirectoryName { get; set; }

        public Guid? ScenarioTemplateId { get; set; }
        public string ScenarioTemplateName { get; set; }
    }

    /// <summary>An Event Template in the file that was not imported, and why. The Id is the one in the file.</summary>
    public record ImportEventTemplateFailure(Guid? Id, string Name, string Reason);

    /// <summary>
    /// An Event Template that was imported with a change, such as a new name or a reference that had to be
    /// cleared. The Id and Name are those of the imported template.
    /// </summary>
    public record ImportEventTemplateWarning(Guid? Id, string Name, string Message);

    public record ImportEventTemplatesResult(
        EventTemplate[] Imported,
        ImportEventTemplateFailure[] Failures,
        ImportEventTemplateWarning[] Warnings);

    public class ImportEventTemplatesForm
    {
        /// <summary>A .json file written by exportEventTemplates, or a .zip archive holding one</summary>
        public IFormFile File { get; set; }

        /// <summary>
        /// If a Player View, Caster Directory or Steamfitter Scenario Template cannot be found by id,
        /// use the one visible to the caller with the exported name, if exactly one has that name
        /// </summary>
        public bool MatchReferencesByName { get; set; }
    }
}
