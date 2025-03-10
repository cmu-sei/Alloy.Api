// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Alloy.Api.Hubs
{
    [Authorize(AuthenticationSchemes = "Bearer")]
    public class EngineHub : Hub
    {
        private readonly AlloyContext _context;
        private readonly IAuthorizationService _authorizationService;
        private readonly ClaimsPrincipal _user;

        public EngineHub(AlloyContext context, IAuthorizationService authorizationService, IPrincipal user)
        {
            _context = context;
            _authorizationService = authorizationService;
            _user = user as ClaimsPrincipal;

        }

        public async Task JoinEvent(Guid eventId)
        {
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
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, eventId.ToString());
        }
        public async Task JoinAdmin()
        {
            //TODO:  replace these commented lines
            // if (!(await _authorizationService.AuthorizeAsync(_user, null, new SystemAdminRightsRequirement())).Succeeded)
            //     throw new ForbiddenException();

            await Groups.AddToGroupAsync(Context.ConnectionId, "admin");
        }
        public async Task LeaveAdmin()
        {
            //TODO:  replace these commented lines
            // if (!(await _authorizationService.AuthorizeAsync(_user, null, new SystemAdminRightsRequirement())).Succeeded)
            //     throw new ForbiddenException();

            await Groups.AddToGroupAsync(Context.ConnectionId, "admin");
        }
    }

    public static class EngineHubMethods
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
        public const string GroupMembershipCreated = "GroupMembershipCreated";
        public const string GroupMembershipUpdated = "GroupMembershipUpdated";
        public const string GroupMembershipDeleted = "GroupMembershipDeleted";
        public const string EventTemplateMembershipCreated = "EventTemplateMembershipCreated";
        public const string EventTemplateMembershipUpdated = "EventTemplateMembershipUpdated";
        public const string EventTemplateMembershipDeleted = "EventTemplateMembershipDeleted";
        public const string EventMembershipCreated = "EventMembershipCreated";
        public const string EventMembershipUpdated = "EventMembershipUpdated";
        public const string EventMembershipDeleted = "EventMembershipDeleted";
    }

}