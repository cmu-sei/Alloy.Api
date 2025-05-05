// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using STT = System.Threading.Tasks;
using System;
using Alloy.Api.Infrastructure.Extensions;

namespace Alloy.Api.Infrastructure.Authorization
{
    public class UserAccessRequirement : IAuthorizationRequirement
    {
        public Guid UserId { get; set; }

        public UserAccessRequirement(Guid userId)
        {
            UserId = userId;
        }
    }

    public class UserAccessHandler : AuthorizationHandler<UserAccessRequirement>, IAuthorizationHandler
    {
        protected override STT.Task HandleRequirementAsync(AuthorizationHandlerContext context, UserAccessRequirement requirement)
        {
            if (context.User.HasClaim(c => c.Type == AlloyClaimTypes.SystemAdmin.ToString()))
            {
                context.Succeed(requirement);
            }
            else if (context.User.GetId() == requirement.UserId)
            {
                context.Succeed(requirement);
            }

            return STT.Task.CompletedTask;
        }
    }
}
