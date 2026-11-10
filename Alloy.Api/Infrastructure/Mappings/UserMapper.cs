// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Alloy.Api.Data.Models;
using Alloy.Api.ViewModels;
using Riok.Mapperly.Abstractions;

namespace Alloy.Api.Infrastructure.Mappings;

/// <summary>Users' mappings.</summary>
/// <remarks>
/// <see cref="User.RoleId"/> is a string: it is written with <c>Guid.ToString()</c> and read with
/// <c>Guid.Parse</c>, so any GUID format is accepted and anything else throws (answered with a 500).
/// </remarks>
[Mapper]
public static partial class UserMapper
{
    public static partial User ToUser(this UserEntity source);

    /// <remarks>Every member is written, the id and audit fields included; <c>AlloyContext</c> adjusts the audit fields as it saves.</remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial UserEntity ToEntity(this User source);

    /// <remarks>
    /// Writes the body's id onto the row as well, so an id other than the route's fails the save
    /// (the id is part of the key).
    /// </remarks>
    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial void ApplyTo(this User source, UserEntity target);
}
