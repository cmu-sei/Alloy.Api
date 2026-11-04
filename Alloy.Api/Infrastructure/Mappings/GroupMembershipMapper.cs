// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using Alloy.Api.Data.Models;
using Alloy.Api.ViewModels;
using Riok.Mapperly.Abstractions;

namespace Alloy.Api.Infrastructure.Mappings;

/// <summary>Group memberships' mappings.</summary>
[Mapper]
public static partial class GroupMembershipMapper
{
    public static partial IQueryable<GroupMembership> ProjectToGroupMembership(this IQueryable<GroupMembershipEntity> query);

    public static partial GroupMembership ToGroupMembership(this GroupMembershipEntity source);

    [MapperRequiredMapping(RequiredMappingStrategy.Source)]
    public static partial GroupMembershipEntity ToEntity(this GroupMembership source);
}
