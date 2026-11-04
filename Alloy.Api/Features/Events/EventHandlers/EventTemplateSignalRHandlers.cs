// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Crucible.Common.EntityEvents.Events;
using Alloy.Api.Hubs;
using Alloy.Api.Infrastructure.Extensions;
using Alloy.Api.Services;
using Alloy.Api.ViewModels;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Alloy.Api.Infrastructure.Mappings;

namespace Alloy.Api.Features.Events.EventHandlers
{
    public class EventTemplateBaseSignlRHandler
    {
        protected readonly AlloyContext _context;
        protected readonly IEventService _eventService;
        protected readonly IHubContext<EngineHub> _engineHub;


        public EventTemplateBaseSignlRHandler(
        AlloyContext context,
        IHubContext<EngineHub> engineHub)
        {
            _context = context;
            _engineHub = engineHub;

        }

        protected async Task HandleCreateOrUpdate(
          EventTemplateEntity eventTemplateEntity,
          string method,
          string[] modifiedProperties,
          CancellationToken ct)
        {

            var alloyEventTemplate = eventTemplateEntity.ToEventTemplate();
            await _engineHub.Clients
                .Groups(eventTemplateEntity.Id.ToString(), EngineHub.ADMIN_EVENT_TEMPLATE_GROUP)
                .SendAsync(method, alloyEventTemplate, modifiedProperties, ct);
        }
    }

    public class EventTemplateCreatedSignalRHandler : EventTemplateBaseSignlRHandler, INotificationHandler<EntityCreated<EventTemplateEntity>>
    {
        public EventTemplateCreatedSignalRHandler(
          AlloyContext context,
          IHubContext<EngineHub> engineHub) : base(context, engineHub) { }

        public async Task Handle(EntityCreated<EventTemplateEntity> notification, CancellationToken ct)
        {
            await base.HandleCreateOrUpdate(notification.Entity, EngineHubMethods.EventTemplateCreated, null, ct);
        }
    }

    public class EventTemplateUpdatedSignalRHandler : EventTemplateBaseSignlRHandler, INotificationHandler<EntityUpdated<EventTemplateEntity>>
    {
        public EventTemplateUpdatedSignalRHandler(
          AlloyContext context,
          IHubContext<EngineHub> engineHub)
           : base(context, engineHub) { }
        public async Task Handle(EntityUpdated<EventTemplateEntity> notification, CancellationToken ct)
        {
            await base.HandleCreateOrUpdate(
              notification.Entity,
              EngineHubMethods.EventTemplateUpdated,
              notification.ModifiedProperties.Select(e => e.TitleCaseToCamelCase()).ToArray(),
              ct);
        }
    }

    public class EventTemplateDeletedSignalRHandler : EventTemplateBaseSignlRHandler, INotificationHandler<EntityDeleted<EventTemplateEntity>>
    {
        public EventTemplateDeletedSignalRHandler(
            AlloyContext context,
            IHubContext<EngineHub> engineHub)
             : base(context, engineHub) { }
        public async Task Handle(EntityDeleted<EventTemplateEntity> notification, CancellationToken ct)
        {
            await _engineHub.Clients
                .Groups(notification.Entity.Id.ToString(), EngineHub.ADMIN_EVENT_TEMPLATE_GROUP)
                .SendAsync(EngineHubMethods.EventDeleted, notification.Entity.Id, ct);
        }
    }

}
