// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Alloy.Api.Infrastructure.Mappings
{
    using Alloy.Api.Data.Models;
    using Alloy.Api.ViewModels;

    public class GroupProfile : AutoMapper.Profile
    {
        public GroupProfile()
        {
            CreateMap<GroupEntity, Group>();
            CreateMap<Group, GroupEntity>();
        }
    }
}