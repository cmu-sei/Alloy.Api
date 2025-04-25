// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Authorization;
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
        private readonly IAlloyAuthorizationService _authorizationService;
        private readonly ClaimsPrincipal _user;
        public const string ADMIN_EVENT_GROUP = "AdminEventGroup";
        public const string ADMIN_EVENT_TEMPLATE_GROUP = "AdminEventTemplateGroup";
        public const string ADMIN_GROUP_GROUP = "AdminGroupGroup";
        public const string ADMIN_ROLE_GROUP = "AdminRoleGroup";
        public const string ADMIN_USER_GROUP = "AdminUserGroup";

        public EngineHub(AlloyContext context, IAlloyAuthorizationService authorizationService, IPrincipal user)
        {
            _context = context;
            _authorizationService = authorizationService;
            _user = user as ClaimsPrincipal;

        }

        public async Task JoinEvent(Guid eventId)
        {
            var userId = _user.GetId();
            var evt = await _context.EventMemberships
                .SingleOrDefaultAsync(x => x.EventId == eventId && x.UserId == userId);

            var ct = new CancellationToken();
            if (evt != null || await _authorizationService.AuthorizeAsync([SystemPermission.ViewEvents], ct))
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
            var userId = _user.GetId();
            var ct = new CancellationToken();
            if (await _authorizationService.AuthorizeAsync([SystemPermission.ViewEvents], ct))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, ADMIN_EVENT_GROUP);
            }
            else
            {
                var eventIds = await _context.EventMemberships
                    .Where(x => x.UserId == userId)
                    .Select(x => x.EventId)
                    .ToListAsync(ct);
                foreach (var item in eventIds)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, item.ToString());
                }
            }
            if (await _authorizationService.AuthorizeAsync([SystemPermission.ViewEventTemplates], ct))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, ADMIN_EVENT_TEMPLATE_GROUP);
            }
            else
            {
                var eventTemplateIds = await _context.EventTemplateMemberships
                    .Where(x => x.UserId == userId)
                    .Select(x => x.EventTemplateId)
                    .ToListAsync(ct);
                foreach (var item in eventTemplateIds)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, item.ToString());
                }
            }
            if (await _authorizationService.AuthorizeAsync([SystemPermission.ViewGroups], ct))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, ADMIN_GROUP_GROUP);
            }
            if (await _authorizationService.AuthorizeAsync([SystemPermission.ViewRoles], ct))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, ADMIN_ROLE_GROUP);
            }
            if (await _authorizationService.AuthorizeAsync([SystemPermission.ViewUsers], ct))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, ADMIN_USER_GROUP);
            }
        }
        public async Task LeaveAdmin()
        {
            var userId = _user.GetId();
            var ct = new CancellationToken();
            if (await _authorizationService.AuthorizeAsync([SystemPermission.ViewEvents], ct))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, ADMIN_EVENT_GROUP);
            }
            else
            {
                var eventIds = await _context.EventMemberships
                    .Where(x => x.UserId == userId)
                    .Select(x => x.EventId)
                    .ToListAsync(ct);
                foreach (var item in eventIds)
                {
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, item.ToString());
                }
            }
            if (await _authorizationService.AuthorizeAsync([SystemPermission.ViewEventTemplates], ct))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, ADMIN_EVENT_TEMPLATE_GROUP);
            }
            else
            {
                var eventTemplateIds = await _context.EventTemplateMemberships
                    .Where(x => x.UserId == userId)
                    .Select(x => x.EventTemplateId)
                    .ToListAsync(ct);
                foreach (var item in eventTemplateIds)
                {
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, item.ToString());
                }
            }
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, ADMIN_GROUP_GROUP);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, ADMIN_ROLE_GROUP);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, ADMIN_USER_GROUP);
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
        public const string GroupCreated = "GroupCreated";
        public const string GroupUpdated = "GroupUpdated";
        public const string GroupDeleted = "GroupDeleted";
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
