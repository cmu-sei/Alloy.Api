// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Alloy.Api.Data;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Alloy.Api.Infrastructure.Authorization
{
    public class EventPermissionRequirement : IAuthorizationRequirement
    {
        public EventPermission[] RequiredPermissions;
        public Guid EventId;

        public EventPermissionRequirement(
            EventPermission[] requiredPermissions,
            Guid projectId)
        {
            RequiredPermissions = requiredPermissions;
            EventId = projectId;
        }
    }

    public class EventPermissionHandler : AuthorizationHandler<EventPermissionRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, EventPermissionRequirement requirement)
        {
            if (context.User == null)
            {
                context.Fail();
            }
            else
            {
                EventPermissionClaim scenarioPermissionsClaim = null;

                var claims = context.User.Claims
                    .Where(x => x.Type == AuthorizationConstants.EventPermissionClaimType)
                    .ToList();

                foreach (var claim in claims)
                {
                    var claimValue = EventPermissionClaim.FromString(claim.Value);
                    if (claimValue.EventId == requirement.EventId)
                    {
                        scenarioPermissionsClaim = claimValue;
                        break;
                    }
                }

                if (scenarioPermissionsClaim == null)
                {
                    context.Fail();
                }
                else if (requirement.RequiredPermissions == null || requirement.RequiredPermissions.Length == 0)
                {
                    context.Succeed(requirement);
                }
                else if (requirement.RequiredPermissions.Any(x => scenarioPermissionsClaim.Permissions.Contains(x)))
                {
                    context.Succeed(requirement);
                }
            }

            return Task.CompletedTask;
        }
    }
}