// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Alloy.Api.Data;
using Alloy.Api.Extensions;
using Alloy.Api.Options;
using Player.Api;
using Player.Api.Client;

namespace Alloy.Api.Services
{
    public interface IUserClaimsService
    {
        Task<ClaimsPrincipal> AddUserClaims(ClaimsPrincipal principal, bool update);
        Task<ClaimsPrincipal> GetClaimsPrincipal(Guid userId, bool setAsCurrent);
        Task<ClaimsPrincipal> RefreshClaims(Guid userId);
        ClaimsPrincipal GetCurrentClaimsPrincipal();
        void SetCurrentClaimsPrincipal(ClaimsPrincipal principal);
        Task<IEnumerable<Claim>> GetUserClaimsAsync(Guid userId, CancellationToken ct);
    }

    public class UserClaimsService : IUserClaimsService
    {
        private readonly AlloyContext _context;
        private readonly ClaimsTransformationOptions _options;
        private readonly IMemoryCache _cache;
        private ClaimsPrincipal _currentClaimsPrincipal;
        private readonly IPlayerApiClient _playerApiClient;

        public UserClaimsService(AlloyContext context, IMemoryCache cache, ClaimsTransformationOptions options, IPlayerApiClient playerApiClient)
        {
            _context = context;
            _options = options;
            _cache = cache;
            _playerApiClient = playerApiClient;
        }

        public async Task<ClaimsPrincipal> AddUserClaims(ClaimsPrincipal principal, bool update)
        {
            List<Claim> claims;
            var identity = (ClaimsIdentity)principal.Identity;
            var userId = principal.GetId();

            if (!_cache.TryGetValue(userId, out claims))
            {
                claims = new List<Claim>();
                claims.AddRange(await GetUserClaims(userId));
                if (_options.EnableCaching)
                {
                    _cache.Set(userId, claims, new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromSeconds(_options.CacheExpirationSeconds)));
                }
            }
            addNewClaims(identity, claims);
            return principal;
        }

        public async Task<ClaimsPrincipal> GetClaimsPrincipal(Guid userId, bool setAsCurrent)
        {
            ClaimsIdentity identity = new ClaimsIdentity();
            identity.AddClaim(new Claim("sub", userId.ToString()));
            ClaimsPrincipal principal = new ClaimsPrincipal(identity);

            principal = await AddUserClaims(principal, false);

            if (setAsCurrent || _currentClaimsPrincipal.GetId() == userId)
            {
                _currentClaimsPrincipal = principal;
            }

            return principal;
        }

        public async Task<ClaimsPrincipal> RefreshClaims(Guid userId)
        {
            _cache.Remove(userId);
            return await GetClaimsPrincipal(userId, false);
        }

        public ClaimsPrincipal GetCurrentClaimsPrincipal()
        {
            return _currentClaimsPrincipal;
        }

        public void SetCurrentClaimsPrincipal(ClaimsPrincipal principal)
        {
            _currentClaimsPrincipal = principal;
        }

        private async Task<IEnumerable<Claim>> GetUserClaims(Guid userId)
        {
            List<Claim> claims = new List<Claim>();
            claims.AddRange(await GetUserClaimsAsync(userId, new CancellationToken()));

            return claims;
        }

        private void addNewClaims(ClaimsIdentity identity, List<Claim> claims)
        {
            var newClaims = new List<Claim>();
            claims.ForEach(delegate (Claim claim)
            {
                if (!identity.Claims.Any(identityClaim => identityClaim.Type == claim.Type))
                {
                    newClaims.Add(claim);
                }
            });
            identity.AddClaims(newClaims);
        }

        public async Task<IEnumerable<Claim>> GetUserClaimsAsync(Guid userId, CancellationToken ct)
        {
            var userClaims = new List<Claim>();
            var user = await _playerApiClient.GetUserAsync((Guid)userId, ct);
            userClaims.Add(new Claim("Name", user.Name));
            userClaims.Add(new Claim(ClaimTypes.Role, user.Name));
            var userPermissions = user.Permissions.Select(p => p.Key);
            foreach (var permission in userPermissions)
            {
                userClaims.Add(new Claim(ClaimTypes.Role, permission));
            }

            return userClaims;
        }
    }
}
