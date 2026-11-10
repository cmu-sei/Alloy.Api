// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Alloy.Api.Data.Models;
using Alloy.Api.ViewModels;
using Riok.Mapperly.Abstractions;

namespace Alloy.Api.Infrastructure.Mappings;

/// <summary>Event templates' mappings.</summary>
[Mapper]
public static partial class EventTemplateMapper
{
    /// <remarks><see cref="EventTemplate.EventTemplatePermissions"/> describes the caller; the controller fills it.</remarks>
    [MapperIgnoreTarget(nameof(EventTemplate.EventTemplatePermissions))]
    public static partial EventTemplate ToEventTemplate(this EventTemplateEntity source);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(EventTemplate.EventTemplatePermissions))]
    public static partial EventTemplateEntity ToEntity(this EventTemplate source);

    /// <remarks>
    /// Writes the body's id and audit fields onto the row as well; <c>AlloyContext</c> adjusts the
    /// audit fields as it saves.
    /// </remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    [MapperIgnoreSource(nameof(EventTemplate.EventTemplatePermissions))]
    public static partial void ApplyTo(this EventTemplate source, EventTemplateEntity target);
}
