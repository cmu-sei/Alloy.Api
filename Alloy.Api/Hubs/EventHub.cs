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
        private readonly IUserClaimsService _claimsService;
        private readonly ClaimsPrincipal _user;



        public EventHub(AlloyContext context, IAuthorizationService authorizationService, IUserClaimsService claimsService, IPrincipal user)
        {
            _context = context;
            _authorizationService = authorizationService;
            _claimsService = claimsService;
            _user = user as ClaimsPrincipal;

        }

        public async Task JoinEvent(Guid eventId)
        {
            var user = await _claimsService.GetClaimsPrincipal(_user.GetId(), true);
            if (!(await _authorizationService.AuthorizeAsync(user, null, new BasicRightsRequirement())).Succeeded)
                throw new ForbiddenException();

            var userEvent = await _context.EventUsers.Where(u => u.EventId == eventId && u.UserId == _user.GetId()).FirstOrDefaultAsync();
            if (userEvent != null)
                await Groups.AddToGroupAsync(Context.ConnectionId, eventId.ToString());
        }
        public async Task LeaveEvent(Guid eventId)
        {
            var user = await _claimsService.GetClaimsPrincipal(_user.GetId(), true);
            if (!(await _authorizationService.AuthorizeAsync(user, null, new BasicRightsRequirement())).Succeeded)
                throw new ForbiddenException();


            await Groups.RemoveFromGroupAsync(Context.ConnectionId, eventId.ToString());
        }
        public async Task JoinAdmin()
        {
            var user = await _claimsService.GetClaimsPrincipal(_user.GetId(), true);
            if (!(await _authorizationService.AuthorizeAsync(user, null, new SystemAdminRightsRequirement())).Succeeded)
                throw new ForbiddenException();
            await Groups.AddToGroupAsync(Context.ConnectionId, "admin");
        }
        public async Task LeaveAdmin()
        {
            var user = await _claimsService.GetClaimsPrincipal(_user.GetId(), true);
            if (!(await _authorizationService.AuthorizeAsync(user, null, new SystemAdminRightsRequirement())).Succeeded)
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