// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.ViewModels;
using Riok.Mapperly.Abstractions;

namespace Alloy.Api.Infrastructure.Mappings;

/// <summary>System roles' mappings. Missing permissions are an empty list in both directions, never null.</summary>
[Mapper]
public static partial class SystemRoleMapper
{
    public static partial SystemRole ToSystemRole(this SystemRoleEntity source);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial SystemRoleEntity ToEntity(this SystemRole source);

    /// <remarks>Writes the body's id onto the row as well, so an id other than the route's fails the save.</remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial void ApplyTo(this SystemRole source, SystemRoleEntity target);

    private static SystemPermission[] MapPermissions(List<SystemPermission> source) =>
        source == null ? [] : source.ToArray();

    private static List<SystemPermission> MapPermissions(SystemPermission[] source) =>
        source == null ? [] : source.ToList();
}
