// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.ViewModels;
using Alloy.Api.Domain.Events;
using Alloy.Api.Services;
using Alloy.Api.Hubs;
using Alloy.Api.Infrastructure.Extensions;

namespace Alloy.Api.Features.Events.EventHandlers
{
  public class EventBaseSignlRHandler
  {
    protected readonly AlloyContext _context;
    protected readonly IMapper _mapper;
    protected readonly IEventService _eventService;
    protected readonly IHubContext<EventHub> _eventHub;
    public EventBaseSignlRHandler(
    AlloyContext context,
    IMapper mapper,
    IHubContext<EventHub> eventHub)
    {
      _context = context;
      _mapper = mapper;
      _eventHub = eventHub;

    }

    protected async Task HandleCreateOrUpdate(
      EventEntity eventEntity,
      string method,
      string[] modifiedProperties,
      CancellationToken ct)
    {

      var alloyEvent = _mapper.Map<Event>(eventEntity);
      var tasks = new List<Task>();

      tasks.Add(_eventHub.Clients.Group(eventEntity.Id.ToString()).SendAsync(method, alloyEvent, modifiedProperties, ct));
      tasks.Add(_eventHub.Clients.Group("admin").SendAsync(method, alloyEvent, modifiedProperties, ct));

      await Task.WhenAll(tasks);
    }
  }

  public class EventCreatedSignalRHandler : EventBaseSignlRHandler, INotificationHandler<EntityCreated<EventEntity>>
  {
    public EventCreatedSignalRHandler(
      AlloyContext context,
      IMapper mapper,
      IHubContext<EventHub> eventHub)
       : base(context, mapper, eventHub) { }

    public async Task Handle(EntityCreated<EventEntity> notification, CancellationToken ct)
    {
      await base.HandleCreateOrUpdate(notification.Entity, EventHubMethods.EventCreated, null, ct);
    }
  }

  public class EventUpdatedSignalRHandler : EventBaseSignlRHandler, INotificationHandler<EntityUpdated<EventEntity>>
  {
    public EventUpdatedSignalRHandler(
      AlloyContext context,
      IMapper mapper,
      IHubContext<EventHub> eventHub

    ) : base(context, mapper, eventHub) { }

    public async Task Handle(EntityUpdated<EventEntity> notification, CancellationToken ct)
    {
      await base.HandleCreateOrUpdate(
        notification.Entity,
        EventHubMethods.EventUpdated,
        notification.ModifiedProperties.Select(e => e.TitleCaseToCamelCase()).ToArray(),
        ct);
    }
  }

  public class VmDeletedSignalRHandler : EventBaseSignlRHandler, INotificationHandler<EntityDeleted<EventEntity>>
  {
    public VmDeletedSignalRHandler(
        AlloyContext context,
        IMapper mapper,
        IHubContext<EventHub> eventHub)
        : base(context, mapper, eventHub)
    { }

    public async Task Handle(EntityDeleted<EventEntity> notification, CancellationToken ct)
    {
      var tasks = new List<Task>();
      tasks.Add(_eventHub.Clients.Group(notification.Entity.Id.ToString()).SendAsync(EventHubMethods.EventDeleted, notification.Entity.Id, ct));
      tasks.Add(_eventHub.Clients.Group("admin").SendAsync(EventHubMethods.EventDeleted, notification.Entity.Id, ct));
      await Task.WhenAll(tasks);
    }
  }
}