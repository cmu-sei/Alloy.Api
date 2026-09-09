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
            // left mapped here is writable by any EditEvent holder - and launching an Event grants
            // its own launcher the Manager role on it, so that includes ordinary self-service
            // owners. Everything ignored below is control state owned by AlloyBackgroundService:
            // the internal status fields drive which terminal status a teardown reports,
            // FailureCount is the retry ceiling and Bootstrap's reclaim gate, and the external
            // resource ids are what teardown deletes - nulling one orphans a Caster Workspace,
            // Player View or Steamfitter Scenario.
            // The list stops where it does because this map is shared: CreateAsync builds a brand
            // new entity through the same reverse direction and does need to seed Status, ViewId,
            // UserId and EventTemplateId. The fields it legitimately seeds but PUT must not change
            // are pinned to their persisted values in EventService.UpdateAsync instead - see the
            // comment there, and the deferred EventUpdateCommand work that would let the two
            // callers stop sharing one map.
            // Ignoring a field here is harmless for the admin UI, which round-trips the whole view
            // model but disables every control except Name, Description and ExpirationDate
            // (event-edit.component.ts setFormDisabled) - the entity keeps its persisted value.
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