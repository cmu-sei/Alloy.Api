// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Rest;
using Steamfitter.Api;
using Steamfitter.Api.Models;
using Alloy.Api.Extensions;
using Alloy.Api.Infrastructure.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alloy.Api.Services
{
    public interface ISteamfitterService
    {
        Task<IEnumerable<ScenarioTemplate>> GetScenarioTemplatesAsync(CancellationToken ct);
    }

    public class SteamfitterService : ISteamfitterService
    {
        private readonly ISteamfitterApiClient _steamfitterApiClient;
        private readonly Guid _userId;

        public SteamfitterService(IHttpContextAccessor httpContextAccessor, ClientOptions clientSettings, ISteamfitterApiClient steamfitterApiClient)
        {
            _userId = httpContextAccessor.HttpContext.User.GetId();
            _steamfitterApiClient = steamfitterApiClient;
        }       

        public async Task<IEnumerable<ScenarioTemplate>> GetScenarioTemplatesAsync(CancellationToken ct)
        {
            var scenarioTemplates = await _steamfitterApiClient.GetScenarioTemplatesAsync(ct);

            return scenarioTemplates;
        }




    }
}

