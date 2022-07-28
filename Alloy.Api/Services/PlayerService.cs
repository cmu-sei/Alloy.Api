// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Extensions;
using Alloy.Api.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Player.Api.Client;

namespace Alloy.Api.Services
{
    public interface IPlayerService
    {
        Task<IEnumerable<View>> GetViewsAsync(CancellationToken ct);
        Task<View> CloneViewAsync(Guid viewId, ViewCloneOverride viewCloneOverride, CancellationToken ct);
        Task DeleteViewAsync(Guid viewId, CancellationToken ct);
        Task<IEnumerable<string>> GetUserClaimValuesAsync(CancellationToken ct);
        Task<User> GetUserAsync(CancellationToken ct);
    }

    public class PlayerService : IPlayerService
    {
        private readonly IPlayerApiClient _playerApiClient;
        private readonly IAuthorizationService _authorizationService;
        private readonly IUserClaimsService _claimsService;
        private readonly ClaimsPrincipal _user;

        public PlayerService(
            IHttpContextAccessor httpContextAccessor,
            IPlayerApiClient playerApiClient,
            IAuthorizationService authorizationService,
            IPrincipal user,
            IUserClaimsService claimsService)
        {
            _playerApiClient = playerApiClient;
            _authorizationService = authorizationService;
            _user = user as ClaimsPrincipal;
            _claimsService = claimsService;
        }

        public async Task<IEnumerable<View>> GetViewsAsync(CancellationToken ct)
        {
            var views = await _playerApiClient.GetUserViewsAsync(_user.GetId(), ct);
            return (IEnumerable<View>)views;
        }

        public async Task<View> CloneViewAsync(Guid viewId, ViewCloneOverride viewCloneOverride, CancellationToken ct)
        {
            return (View)await _playerApiClient.CloneViewAsync(viewId, viewCloneOverride, ct);
        }

        public async Task DeleteViewAsync(Guid viewId, CancellationToken ct)
        {
            await _playerApiClient.DeleteViewAsync(viewId);
        }

        public async Task<IEnumerable<string>> GetUserClaimValuesAsync(CancellationToken ct)
        {
            var claimValues = new List<string>();
            if ((await _authorizationService.AuthorizeAsync(_user, null, new SystemAdminRightsRequirement())).Succeeded) claimValues.Add("SystemAdmin");
            if ((await _authorizationService.AuthorizeAsync(_user, null, new ContentDeveloperRightsRequirement())).Succeeded) claimValues.Add("ContentDeveloper");

            return claimValues;
        }

        public async Task<User> GetUserAsync(CancellationToken ct)
        {
            var user = await _playerApiClient.GetUserAsync(_user.GetId());
            return user;
        }

    }
}
