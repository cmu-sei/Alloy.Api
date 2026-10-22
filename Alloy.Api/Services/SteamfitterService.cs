// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Alloy.Api.Infrastructure.Extensions;
using Alloy.Api.Infrastructure.Options;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Steamfitter.Api.Client;

namespace Alloy.Api.Services
{
    public interface ISteamfitterService
    {
        Task<IEnumerable<ScenarioTemplate>> GetScenarioTemplatesAsync(CancellationToken ct);
        Task<ScenarioTemplate> GetScenarioTemplateAsync(Guid scenarioTemplateId, CancellationToken ct);
    }

    public class SteamfitterService : ISteamfitterService
    {
        private readonly ISteamfitterApiClient _steamfitterApiClient;
        private readonly Guid _userId;
        private readonly ILogger<SteamfitterService> _logger;

        public SteamfitterService(IHttpContextAccessor httpContextAccessor, ClientOptions clientSettings, ISteamfitterApiClient steamfitterApiClient,
            ILogger<SteamfitterService> logger)
        {
            _logger = logger;
            _userId = httpContextAccessor.HttpContext.User.GetId();
            _steamfitterApiClient = steamfitterApiClient;
        }

        public async Task<IEnumerable<ScenarioTemplate>> GetScenarioTemplatesAsync(CancellationToken ct)
        {
            return await _logger.CallUpstreamAsync("Steamfitter", "load the Steamfitter scenario templates",
                async () => await _steamfitterApiClient.GetScenarioTemplatesAsync(ct), ct);
        }

        public async Task<ScenarioTemplate> GetScenarioTemplateAsync(Guid scenarioTemplateId, CancellationToken ct)
        {
            return await _steamfitterApiClient.GetScenarioTemplateAsync(scenarioTemplateId, ct);
        }




    }
}
