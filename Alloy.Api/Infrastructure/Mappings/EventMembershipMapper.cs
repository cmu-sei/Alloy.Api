// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Alloy.Api.Data.Models;
using Alloy.Api.ViewModels;
using Riok.Mapperly.Abstractions;

namespace Alloy.Api.Infrastructure.Mappings;

/// <summary>Event memberships' mappings.</summary>
[Mapper]
public static partial class EventMembershipMapper
{
    public static partial EventMembership ToEventMembership(this EventMembershipEntity source);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial EventMembershipEntity ToEntity(this EventMembership source);
}
