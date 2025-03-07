// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoMapper;
using Alloy.Api.ViewModels;
using Alloy.Api.Data.Models;

namespace Alloy.Api.Infrastructure.Mapping
{
    public class EventRoleProfile : Profile
    {
        public EventRoleProfile()
        {
            CreateMap<EventRoleEntity, EventRole>();
        }
    }
}