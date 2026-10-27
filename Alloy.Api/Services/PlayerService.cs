// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Infrastructure.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Player.Api.Client;

namespace Alloy.Api.Services
{
    public interface IPlayerService
    {
        Task<IEnumerable<View>> GetViewsAsync(CancellationToken ct);
        Task<IEnumerable<View>> GetVisibleViewsAsync(CancellationToken ct);
        Task<View> GetViewAsync(Guid viewId, CancellationToken ct);
        Task<View> CloneViewAsync(Guid viewId, CloneViewCommand cloneViewCommand, CancellationToken ct);
        Task DeleteViewAsync(Guid viewId, CancellationToken ct);
    }

    public class PlayerService : IPlayerService
    {
        private readonly IPlayerApiClient _playerApiClient;
        private readonly IAuthorizationService _authorizationService;
        private readonly IUserClaimsService _claimsService;
        private readonly ClaimsPrincipal _user;
        private readonly ILogger<PlayerService> _logger;

        public PlayerService(
            IHttpContextAccessor httpContextAccessor,
            IPlayerApiClient playerApiClient,
            IAuthorizationService authorizationService,
            IPrincipal user,
            IUserClaimsService claimsService,
            ILogger<PlayerService> logger)
        {
            _logger = logger;
            _playerApiClient = playerApiClient;
            _authorizationService = authorizationService;
            _user = user as ClaimsPrincipal;
            _claimsService = claimsService;
        }

        public async Task<IEnumerable<View>> GetViewsAsync(CancellationToken ct)
        {
            return await _logger.CallUpstreamAsync("Player", "load your Player views",
                async () => await _playerApiClient.GetUserViewsAsync(_user.GetId(), ct), ct);
        }

        /// <summary>
        /// Every Player view the user can see: all of them when Player grants the user ViewViews, otherwise the
        /// views the user is a member of. Player answers its list of all views with a 403 to anyone else.
        /// </summary>
        public async Task<IEnumerable<View>> GetVisibleViewsAsync(CancellationToken ct)
        {
            try
            {
                return await _playerApiClient.GetViewsAsync(ct);
            }
            catch (Exception ex) when (ApiFailureExtensions.StatusCode(ex) == (int)HttpStatusCode.Forbidden)
            {
                return await GetViewsAsync(ct);
            }
        }

        public async Task<View> GetViewAsync(Guid viewId, CancellationToken ct)
        {
            return await _playerApiClient.GetViewAsync(viewId, ct);
        }

        public async Task<View> CloneViewAsync(Guid viewId, CloneViewCommand cloneViewCommand, CancellationToken ct)
        {
            return await _playerApiClient.CloneViewAsync(viewId, cloneViewCommand, ct);
        }

        public async Task DeleteViewAsync(Guid viewId, CancellationToken ct)
        {
            await _playerApiClient.DeleteViewAsync(viewId);
        }

    }
}
