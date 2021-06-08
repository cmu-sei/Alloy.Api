// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Alloy.Api.Services;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Alloy.Api.Infrastructure.ClaimsTransformers
{
    class AuthorizationClaimsTransformer : IClaimsTransformation
    {
        private readonly IUserClaimsService _claimsService;

        public AuthorizationClaimsTransformer(IUserClaimsService claimsService)
        {
            _claimsService = claimsService;
        }

        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            var user = await _claimsService.AddUserClaims(principal, true);
            _claimsService.SetCurrentClaimsPrincipal(user);
            return user;
        }
    }
}
