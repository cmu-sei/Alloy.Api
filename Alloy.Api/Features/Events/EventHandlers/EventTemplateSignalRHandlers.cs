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
    public class EventTemplateBaseSignlRHandler
    {
        protected readonly AlloyContext _context;
        protected readonly IMapper _mapper;
        protected readonly IEventService _eventService;
        protected readonly IHubContext<EventHub> _eventHub;


        public EventTemplateBaseSignlRHandler(
        AlloyContext context,
        IMapper mapper,
        IHubContext<EventHub> eventHub)
        {
            _context = context;
            _mapper = mapper;
            _eventHub = eventHub;

        }

        protected async Task HandleCreateOrUpdate(
          EventTemplateEntity eventTemplateEntity,
          string method,
          string[] modifiedProperties,
          CancellationToken ct)
        {

            var alloyEventTemplate = _mapper.Map<EventTemplate>(eventTemplateEntity);
            var tasks = new List<Task>();
            tasks.Add(_eventHub.Clients.Group(eventTemplateEntity.Id.ToString()).SendAsync(method, alloyEventTemplate, modifiedProperties, ct));
            tasks.Add(_eventHub.Clients.Group("admin").SendAsync(method, alloyEventTemplate, modifiedProperties, ct));

            await Task.WhenAll(tasks);
        }
    }

    public class EventTemplateCreatedSignalRHandler : EventTemplateBaseSignlRHandler, INotificationHandler<EntityCreated<EventTemplateEntity>>
    {
        public EventTemplateCreatedSignalRHandler(
          AlloyContext context,
          IMapper mapper,
          IHubContext<EventHub> eventHub) : base(context, mapper, eventHub) { }

        public async Task Handle(EntityCreated<EventTemplateEntity> notification, CancellationToken ct)
        {
            await base.HandleCreateOrUpdate(notification.Entity, EventHubMethods.EventTemplateCreated, null, ct);
        }
    }

    public class EventTemplateUpdatedSignalRHandler : EventTemplateBaseSignlRHandler, INotificationHandler<EntityUpdated<EventTemplateEntity>>
    {
        public EventTemplateUpdatedSignalRHandler(
          AlloyContext context,
          IMapper mapper,
          IHubContext<EventHub> eventHub)
           : base(context, mapper, eventHub) { }
        public async Task Handle(EntityUpdated<EventTemplateEntity> notification, CancellationToken ct)
        {
            await base.HandleCreateOrUpdate(
              notification.Entity,
              EventHubMethods.EventTemplateUpdated,
              notification.ModifiedProperties.Select(e => e.TitleCaseToCamelCase()).ToArray(),
              ct);
        }
    }

    public class EventTemplateDeletedSignalRHandler : EventTemplateBaseSignlRHandler, INotificationHandler<EntityDeleted<EventTemplateEntity>>
    {
        public EventTemplateDeletedSignalRHandler(
            AlloyContext context,
            IMapper mapper,
            IHubContext<EventHub> eventHub)
             : base(context, mapper, eventHub) { }
        public async Task Handle(EntityDeleted<EventTemplateEntity> notification, CancellationToken ct)
        {
            var tasks = new List<Task>();
            tasks.Add(_eventHub.Clients.Group(notification.Entity.Id.ToString()).SendAsync(EventHubMethods.EventDeleted, notification.Entity.Id, ct));
            tasks.Add(_eventHub.Clients.Group("admin").SendAsync(EventHubMethods.EventDeleted, notification.Entity.Id, ct));
            await Task.WhenAll(tasks);
        }
    }

}