// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using Alloy.Api.Data;
using System.Security.Claims;

namespace Alloy.Api.Infrastructure.Authorization
{
    public class BasicRightsRequirement : IAuthorizationRequirement
    {
    }

    public class BasicRightsHandler : AuthorizationHandler<BasicRightsRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, BasicRightsRequirement requirement)
        {
            // TODO: IF we want to limit who can start an odx/lab, remove 'true' from the condition and give 'AlloyBasic' permission to allowed users
            if(true
                || context.User.HasClaim(ClaimTypes.Role, AlloyClaimTypes.AlloyBasic.ToString())
                || context.User.HasClaim(ClaimTypes.Role, AlloyClaimTypes.ContentDeveloper.ToString())
                || context.User.HasClaim(ClaimTypes.Role, AlloyClaimTypes.SystemAdmin.ToString()))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}

