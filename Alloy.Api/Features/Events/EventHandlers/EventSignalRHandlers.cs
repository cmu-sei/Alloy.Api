// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
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
using AutoMapper.QueryableExtensions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Alloy.Api.Features.Events.EventHandlers
{
    public class EventBaseSignlRHandler
    {
        protected readonly AlloyContext _context;
        protected readonly IMapper _mapper;
        protected readonly IEventService _eventService;
        protected readonly IHubContext<EngineHub> _engineHub;
        public EventBaseSignlRHandler(
        AlloyContext context,
        IMapper mapper,
        IHubContext<EngineHub> engineHub)
        {
            _context = context;
            _mapper = mapper;
            _engineHub = engineHub;

        }

        protected async Task HandleCreateOrUpdate(
          EventEntity eventEntity,
          string method,
          string[] modifiedProperties,
          CancellationToken ct)
        {

            var alloyEvent = _mapper.Map<Event>(eventEntity);
            await _engineHub.Clients
                .Groups(eventEntity.Id.ToString(), EngineHub.ADMIN_EVENT_GROUP)
                .SendAsync(method, alloyEvent, modifiedProperties, ct);
        }
    }

    public class EventCreatedSignalRHandler : EventBaseSignlRHandler, INotificationHandler<EntityCreated<EventEntity>>
    {
        public EventCreatedSignalRHandler(
          AlloyContext context,
          IMapper mapper,
          IHubContext<EngineHub> engineHub)
           : base(context, mapper, engineHub) { }

        public async Task Handle(EntityCreated<EventEntity> notification, CancellationToken ct)
        {
            await base.HandleCreateOrUpdate(notification.Entity, EngineHubMethods.EventCreated, null, ct);
        }
    }

    public class EventUpdatedSignalRHandler : EventBaseSignlRHandler, INotificationHandler<EntityUpdated<EventEntity>>
    {
        public EventUpdatedSignalRHandler(
          AlloyContext context,
          IMapper mapper,
          IHubContext<EngineHub> engineHub

        ) : base(context, mapper, engineHub) { }

        public async Task Handle(EntityUpdated<EventEntity> notification, CancellationToken ct)
        {
            await base.HandleCreateOrUpdate(
              notification.Entity,
              EngineHubMethods.EventUpdated,
              notification.ModifiedProperties.Select(e => e.TitleCaseToCamelCase()).ToArray(),
              ct);
        }
    }

    public class EventDeletedSignalRHandler : EventBaseSignlRHandler, INotificationHandler<EntityDeleted<EventEntity>>
    {
        public EventDeletedSignalRHandler(
            AlloyContext context,
            IMapper mapper,
            IHubContext<EngineHub> engineHub)
            : base(context, mapper, engineHub)
        { }

        public async Task Handle(EntityDeleted<EventEntity> notification, CancellationToken ct)
        {
            await _engineHub.Clients
                .Groups(notification.Entity.Id.ToString(), EngineHub.ADMIN_EVENT_GROUP)
                .SendAsync(EngineHubMethods.EventDeleted, notification.Entity.Id, ct);
        }
    }
}
