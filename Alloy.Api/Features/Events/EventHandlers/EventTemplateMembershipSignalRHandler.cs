// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Alloy.Api.Data.Models;
using Alloy.Api.Domain.Events;
using Alloy.Api.Hubs;

namespace Alloy.Api.Events.EventHandlers
{
    public class EventTemplateMembershipCreatedSignalRHandler : INotificationHandler<EntityCreated<EventTemplateMembershipEntity>>
    {
        private readonly IHubContext<EngineHub> _engineHub;
        private readonly IMapper _mapper;

        public EventTemplateMembershipCreatedSignalRHandler(
            IHubContext<EngineHub> engineHub,
            IMapper mapper)
        {
            _engineHub = engineHub;
            _mapper = mapper;
        }

        public async Task Handle(EntityCreated<EventTemplateMembershipEntity> notification, CancellationToken cancellationToken)
        {
            var groupMembership = _mapper.Map<ViewModels.EventTemplateMembership>(notification.Entity);
            await _engineHub.Clients
                .Groups(notification.Entity.Id.ToString(), EngineHub.ADMIN_EVENT_TEMPLATE_GROUP)
                .SendAsync(EngineHubMethods.EventTemplateMembershipCreated, groupMembership);
        }
    }

    public class EventTemplateMembershipDeletedSignalRHandler : INotificationHandler<EntityDeleted<EventTemplateMembershipEntity>>
    {
        private readonly IHubContext<EngineHub> _engineHub;

        public EventTemplateMembershipDeletedSignalRHandler(
            IHubContext<EngineHub> engineHub)
        {
            _engineHub = engineHub;
        }

        public async Task Handle(EntityDeleted<EventTemplateMembershipEntity> notification, CancellationToken cancellationToken)
        {
            await _engineHub.Clients
                .Groups(notification.Entity.Id.ToString(), EngineHub.ADMIN_EVENT_TEMPLATE_GROUP)
                .SendAsync(EngineHubMethods.EventTemplateMembershipDeleted, notification.Entity.Id);
        }
    }

    public class EventTemplateMembershipUpdatedSignalRHandler : INotificationHandler<EntityUpdated<EventTemplateMembershipEntity>>
    {
        private readonly IHubContext<EngineHub> _engineHub;
        private readonly IMapper _mapper;

        public EventTemplateMembershipUpdatedSignalRHandler(
            IHubContext<EngineHub> engineHub,
            IMapper mapper)
        {
            _engineHub = engineHub;
            _mapper = mapper;
        }

        public async Task Handle(EntityUpdated<EventTemplateMembershipEntity> notification, CancellationToken cancellationToken)
        {
            var groupMembership = _mapper.Map<ViewModels.EventTemplateMembership>(notification.Entity);
            await _engineHub.Clients
                .Groups(notification.Entity.Id.ToString(), EngineHub.ADMIN_EVENT_TEMPLATE_GROUP)
                .SendAsync(EngineHubMethods.EventTemplateMembershipUpdated, groupMembership);
        }
    }
}
