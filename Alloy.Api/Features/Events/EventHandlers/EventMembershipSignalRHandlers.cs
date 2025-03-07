// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Domain.Events;
using Alloy.Api.Hubs;
using Alloy.Api.Infrastructure.Extensions;
using Alloy.Api.Services;
using Alloy.Api.ViewModels;
using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace Alloy.Api.Features.Events.EventHandlers
{
    public class EventMembershipBaseSignalRHandler
    {
        protected readonly AlloyContext _context;
        protected readonly IMapper _mapper;
        protected readonly IEventMembershipService _eventService;
        protected readonly IHubContext<EngineHub> _engineHub;
        public EventMembershipBaseSignalRHandler(
        AlloyContext context,
        IMapper mapper,
        IHubContext<EngineHub> engineHub)
        {
            _context = context;
            _mapper = mapper;
            _engineHub = engineHub;

        }

        protected async Task HandleCreateOrUpdate(
          EventMembershipEntity eventEntity,
          string method,
          string[] modifiedProperties,
          CancellationToken ct)
        {

            var eventMembership = _mapper.Map<EventMembership>(eventEntity);
            await _engineHub.Clients
                .Groups(eventEntity.Id.ToString(), "admin")
                .SendAsync(method, eventMembership, modifiedProperties, ct);
        }
    }

    public class EventMembershipCreatedSignalRHandler : EventMembershipBaseSignalRHandler, INotificationHandler<EntityCreated<EventMembershipEntity>>
    {
        public EventMembershipCreatedSignalRHandler(
          AlloyContext context,
          IMapper mapper,
          IHubContext<EngineHub> engineHub)
           : base(context, mapper, engineHub) { }

        public async Task Handle(EntityCreated<EventMembershipEntity> notification, CancellationToken ct)
        {
            await base.HandleCreateOrUpdate(notification.Entity, EngineHubMethods.EventMembershipCreated, null, ct);
        }
    }

    public class EventMembershipUpdatedSignalRHandler : EventMembershipBaseSignalRHandler, INotificationHandler<EntityUpdated<EventMembershipEntity>>
    {
        public EventMembershipUpdatedSignalRHandler(
          AlloyContext context,
          IMapper mapper,
          IHubContext<EngineHub> engineHub

        ) : base(context, mapper, engineHub) { }

        public async Task Handle(EntityUpdated<EventMembershipEntity> notification, CancellationToken ct)
        {
            await base.HandleCreateOrUpdate(
              notification.Entity,
              EngineHubMethods.EventMembershipUpdated,
              notification.ModifiedProperties.Select(e => e.TitleCaseToCamelCase()).ToArray(),
              ct);
        }
    }

    public class EventMembershipDeletedSignalRHandler : EventMembershipBaseSignalRHandler, INotificationHandler<EntityDeleted<EventMembershipEntity>>
    {
        public EventMembershipDeletedSignalRHandler(
            AlloyContext context,
            IMapper mapper,
            IHubContext<EngineHub> engineHub)
            : base(context, mapper, engineHub)
        { }

        public async Task Handle(EntityDeleted<EventMembershipEntity> notification, CancellationToken ct)
        {
            await _engineHub.Clients
                .Groups(notification.Entity.Id.ToString(), "admin")
                .SendAsync(EngineHubMethods.EventMembershipDeleted, notification.Entity.Id, ct);
        }
    }
}