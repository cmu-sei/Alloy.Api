// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using Alloy.Api.Data;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Alloy.Api.Infrastructure.Authorization
{
    public class SystemAdminRightsRequirement : IAuthorizationRequirement
    {
    }

    public class SystemAdminRightsHandler : AuthorizationHandler<SystemAdminRightsRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SystemAdminRightsRequirement requirement)
        {
            // SystemAdmins must have the SystemAdmin claim
            if(context.User.HasClaim(ClaimTypes.Role, AlloyClaimTypes.SystemAdmin.ToString()))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}

