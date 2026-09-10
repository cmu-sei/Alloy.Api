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
            // CreateAsync uses this reverse map. Lifecycle bookkeeping is server-owned;
            // UpdateAsync assigns only the editable fields instead of using this map.
            .ForMember(x => x.EndRequestedAt, opt => opt.Ignore())
            .ForMember(x => x.ErrorMessage, opt => opt.Ignore())
            .ForMember(x => x.InternalStatus, opt => opt.Ignore())
            .ForMember(x => x.FailureCount, opt => opt.Ignore())
            .ForMember(x => x.LastLaunchStatus, opt => opt.Ignore())
            .ForMember(x => x.LastLaunchInternalStatus, opt => opt.Ignore())
            .ForMember(x => x.LastEndStatus, opt => opt.Ignore())
            .ForMember(x => x.LastEndInternalStatus, opt => opt.Ignore())
            .ForMember(x => x.WorkspaceId, opt => opt.Ignore())
            .ForMember(x => x.RunId, opt => opt.Ignore())
            .ForMember(x => x.ScenarioId, opt => opt.Ignore());
        }
    }
}
