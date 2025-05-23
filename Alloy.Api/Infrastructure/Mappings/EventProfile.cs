using System.Security.Claims;
// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Alloy.Api.Data.Models;
using Alloy.Api.ViewModels;

namespace Alloy.Api.Infrastructure.Mappings
{
    public class EventProfile : AutoMapper.Profile
    {
        public EventProfile()
        {
            CreateMap<EventEntity, Event>()
            .ReverseMap();
        }
    }
}