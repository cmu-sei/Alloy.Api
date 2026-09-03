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
            .ReverseMap()
            // PUT /api/events/{id} maps the whole view model back onto the entity, so anything
            // left mapped here is writable by any EditEvent holder. ErrorMessage is owned by
            // AlloyBackgroundService and must not be overwritten from a request body.
            // Scoped to ErrorMessage only on purpose: the admin edit dialog deliberately PUTs
            // Status, and event-list.component.ts round-trips the entire view model, so a broader
            // lockdown here would break the admin UI. See the deferred EventUpdateCommand work.
            .ForMember(x => x.ErrorMessage, opt => opt.Ignore());
        }
    }
}