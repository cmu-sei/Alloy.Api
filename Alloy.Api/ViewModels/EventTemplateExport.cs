// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Microsoft.AspNetCore.Http;

namespace Alloy.Api.ViewModels
{
    /// <summary>
    /// One Event Template as written to event_templates.json in an export archive.
    /// </summary>
    /// <remarks>
    /// The Player View, Caster Directory and Steamfitter Scenario Template live in other applications,
    /// so only their ids travel. Each id is paired with the name it had at export time, so an import into
    /// another environment can match it by name and can say which reference it could not resolve.
    /// Memberships are not exported: users and groups are specific to the environment.
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

    public enum ImportEventTemplateFailureType
    {
        EventTemplateExists,
        Other
    }

    /// <summary>An Event Template that was not imported, and why.</summary>
    public record ImportEventTemplateFailure(Guid? Id, string Name, string Reason,
        ImportEventTemplateFailureType FailureType = ImportEventTemplateFailureType.Other);

    /// <summary>An Event Template that was imported with a change, such as a reference that had to be cleared.</summary>
    public record ImportEventTemplateWarning(Guid? Id, string Name, string Message);

    public record ImportEventTemplatesResult(
        EventTemplate[] Imported,
        ImportEventTemplateFailure[] Failures,
        ImportEventTemplateWarning[] Warnings);

    public class ImportEventTemplatesForm
    {
        /// <summary>A .zip, .tar.gz or .tgz archive created by exportEventTemplates</summary>
        public IFormFile Archive { get; set; }

        /// <summary>
        /// If a Player View, Caster Directory or Steamfitter Scenario Template cannot be found by id,
        /// use the one visible to the caller with the exported name, if exactly one has that name
        /// </summary>
        public bool MatchReferencesByName { get; set; }
    }
}
