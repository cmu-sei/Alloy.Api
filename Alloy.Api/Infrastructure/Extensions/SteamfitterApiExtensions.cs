// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IdentityModel.Client;
using Alloy.Api.Data.Models;
using Steamfitter.Api.Client;
using System.Collections.Generic;

namespace Alloy.Api.Infrastructure.Extensions
{
    public static class SteamfitterApiExtensions
    {
        public static SteamfitterApiClient GetSteamfitterApiClient(IHttpClientFactory httpClientFactory, string apiUrl, TokenResponse tokenResponse)
        {
            var client = ApiClientsExtensions.GetHttpClient(httpClientFactory, apiUrl, tokenResponse);
            var apiClient = new SteamfitterApiClient(client);
            return apiClient;
        }

        public static async Task<Scenario> CreateSteamfitterScenarioAsync(SteamfitterApiClient steamfitterApiClient, EventEntity eventEntity, Guid scenarioTemplateId, CancellationToken ct)
        {
            try
            {
                var options = new ScenarioCloneOptions
                {
                    ViewId = eventEntity.ViewId,
                    NameSuffix = $"- {eventEntity.Username}",
                    UserIds = new List<Guid>() { eventEntity.UserId }
                };

                var scenario = await steamfitterApiClient.CreateScenarioFromScenarioTemplateAsync(scenarioTemplateId, options, ct);
                return scenario;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static async Task<bool> StartSteamfitterScenarioAsync(SteamfitterApiClient steamfitterApiClient, Guid scenarioId, CancellationToken ct)
        {
            try
            {
                await steamfitterApiClient.StartScenarioAsync(scenarioId, ct);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static async Task<bool> EndSteamfitterScenarioAsync(string steamfitterApiUrl, Guid? scenarioId, SteamfitterApiClient steamfitterApiClient, CancellationToken ct)
        {
            // no scenario to end
            if (scenarioId == null)
            {
                return true;
            }
            try
            {
                await steamfitterApiClient.EndScenarioAsync((Guid)scenarioId, ct);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

    }
}
