// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.ViewModels;
using Riok.Mapperly.Abstractions;

namespace Alloy.Api.Infrastructure.Mappings;

/// <summary>Event roles' mappings. Missing permissions are an empty list, never null.</summary>
[Mapper]
public static partial class EventRoleMapper
{
    public static partial EventRole ToEventRole(this EventRoleEntity source);

    private static EventPermission[] MapPermissions(List<EventPermission> source) =>
        source == null ? [] : source.ToArray();
}
