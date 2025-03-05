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
    public class EventTemplatePermissionRequirement : IAuthorizationRequirement
    {
        public EventTemplatePermission[] RequiredPermissions;
        public Guid EventId;

        public EventTemplatePermissionRequirement(
            EventTemplatePermission[] requiredPermissions,
            Guid projectId)
        {
            RequiredPermissions = requiredPermissions;
            EventId = projectId;
        }
    }

    public class EventTemplatePermissionHandler : AuthorizationHandler<EventTemplatePermissionRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, EventTemplatePermissionRequirement requirement)
        {
            if (context.User == null)
            {
                context.Fail();
            }
            else
            {
                EventTemplatePermissionClaim scenarioTemplatePermissionsClaim = null;

                var claims = context.User.Claims
                    .Where(x => x.Type == AuthorizationConstants.EventTemplatePermissionClaimType)
                    .ToList();

                foreach (var claim in claims)
                {
                    var claimValue = EventTemplatePermissionClaim.FromString(claim.Value);
                    if (claimValue.EventTemplateId == requirement.EventId)
                    {
                        scenarioTemplatePermissionsClaim = claimValue;
                        break;
                    }
                }

                if (scenarioTemplatePermissionsClaim == null)
                {
                    context.Fail();
                }
                else if (requirement.RequiredPermissions == null || requirement.RequiredPermissions.Length == 0)
                {
                    context.Succeed(requirement);
                }
                else if (requirement.RequiredPermissions.Any(x => scenarioTemplatePermissionsClaim.Permissions.Contains(x)))
                {
                    context.Succeed(requirement);
                }
            }

            return Task.CompletedTask;
        }
    }
}