// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Extensions;
using Alloy.Api.Infrastructure.Authorization;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.Infrastructure.Extensions;
using Alloy.Api.Infrastructure.Mappings;
using Alloy.Api.Infrastructure.Options;
using Alloy.Api.Services;
using Alloy.Api.ViewModels;
using AutoMapper;
using Caster.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Alloy.Api.Hubs
{
    [Authorize(AuthenticationSchemes = "Bearer")]
    public class EventHub : Hub
    {
        private readonly AlloyContext _context;
        private readonly IAuthorizationService _authorizationService;
        private readonly ClaimsPrincipal _user;

        public EventHub(AlloyContext context, IAuthorizationService authorizationService, IPrincipal user)
        {
            _context = context;
            _authorizationService = authorizationService;
            _user = user as ClaimsPrincipal;

        }

        public async Task JoinEvent(Guid eventId)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new BasicRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            var evt = await _context.Events
                .Include(x => x.EventUsers)
                .SingleOrDefaultAsync(x => x.Id == eventId);

            if (evt.UserId == _user.GetId() || evt.EventUsers.Any(x => x.UserId == _user.GetId()))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, eventId.ToString());
            }
        }
        public async Task LeaveEvent(Guid eventId)
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new BasicRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, eventId.ToString());
        }
        public async Task JoinAdmin()
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new SystemAdminRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            await Groups.AddToGroupAsync(Context.ConnectionId, "admin");
        }
        public async Task LeaveAdmin()
        {
            if (!(await _authorizationService.AuthorizeAsync(_user, null, new SystemAdminRightsRequirement())).Succeeded)
                throw new ForbiddenException();

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