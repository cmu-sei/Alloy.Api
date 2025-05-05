// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using Alloy.Api.ViewModels;

namespace Alloy.Api.Infrastructure.Mappings
{

    public class SystemRoleProfile : Profile
    {
        public SystemRoleProfile()
        {
            CreateMap<Data.Models.SystemRoleEntity, SystemRole>();
            CreateMap<SystemRole, Data.Models.SystemRoleEntity>();
        }
    }
}