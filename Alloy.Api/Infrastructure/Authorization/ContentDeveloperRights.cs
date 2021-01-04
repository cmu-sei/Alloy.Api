// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using Alloy.Api.Data;
using System.Security.Claims;

namespace Alloy.Api.Infrastructure.Authorization
{
    public class ContentDeveloperRightsRequirement : IAuthorizationRequirement
    {
    }

    public class ContentDeveloperRightsHandler : AuthorizationHandler<ContentDeveloperRightsRequirement>, IAuthorizationHandler
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ContentDeveloperRightsRequirement requirement)
        {
            if(context.User.HasClaim(ClaimTypes.Role, AlloyClaimTypes.ContentDeveloper.ToString()))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}

