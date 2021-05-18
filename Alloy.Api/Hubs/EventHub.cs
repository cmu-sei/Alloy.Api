// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Alloy.Api.Hubs
{
  [Authorize(AuthenticationSchemes = "Bearer")]
  public class EventHub : Hub
  {
    private readonly AlloyContext _context;

    public EventHub(AlloyContext context)
    {
      _context = context;
    }

    public async Task JoinEvent(Guid eventId)
    {
      await Groups.AddToGroupAsync(Context.ConnectionId, eventId.ToString());
    }
    public async Task LeaveEvent(Guid eventId)
    {
      await Groups.RemoveFromGroupAsync(Context.ConnectionId, eventId.ToString());
    }
    public async Task JoinAdmin()
    {
      await Groups.AddToGroupAsync(Context.ConnectionId, "admin");
    }
    public async Task LeaveAdmin()
    {
      await Groups.AddToGroupAsync(Context.ConnectionId, "admin");
    }
  }

  public static class EventHubMethods
  {
    public const string EventCreated = "EventCreated";
    public const string EventUpdated = "EventUpdated";
    public const string EventDeleted = "EventDeleted";
    public const string EventTemplateCreated = "EventTemplateCreated";
    public const string EventTemplateUpdated = "EventTemplateUpdated";
    public const string EventTemplateDeleted = "EventTemplateDeleted";
    public const string EventUserCreated = "EventUserCreated";
    public const string EventUserUpdated = "EventUserUpdated";
    public const string EventUserDeleted = "EventUserDeleted";
  }
}