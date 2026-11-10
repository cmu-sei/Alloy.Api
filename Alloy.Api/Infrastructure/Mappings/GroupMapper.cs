// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Alloy.Api.Data.Models;
using Alloy.Api.ViewModels;
using Riok.Mapperly.Abstractions;

namespace Alloy.Api.Infrastructure.Mappings;

/// <summary>Groups' mappings. The view model has no description, so the stored one is kept on update.</summary>
[Mapper]
public static partial class GroupMapper
{
    public static partial Group ToGroup(this GroupEntity source);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial GroupEntity ToEntity(this Group source);

    /// <remarks>Writes the body's id onto the row as well, so an id other than the route's fails the save.</remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial void ApplyTo(this Group source, GroupEntity target);
}
