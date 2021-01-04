// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IdentityModel.Client;
using Steamfitter.Api;
using Steamfitter.Api.Models;
using Alloy.Api.Data.Models;

namespace Alloy.Api.Infrastructure.Extensions
{
    public static class SteamfitterApiExtensions
    {
        public static SteamfitterApiClient GetSteamfitterApiClient(IHttpClientFactory httpClientFactory, string apiUrl, TokenResponse tokenResponse)
        {
            var client = ApiClientsExtensions.GetHttpClient(httpClientFactory, apiUrl, tokenResponse);
            var apiClient = new SteamfitterApiClient(client, true);
            apiClient.BaseUri = client.BaseAddress;
            return apiClient;
        }

        public static async Task<Scenario> CreateSteamfitterScenarioAsync(SteamfitterApiClient steamfitterApiClient, EventEntity eventEntity, Guid scenarioTemplateId, CancellationToken ct)
        {
            try
            {
                var scenario = await steamfitterApiClient.CreateScenarioFromScenarioTemplateAsync(scenarioTemplateId, ct);
                scenario.Name = $"{scenario.Name.Replace("From ScenarioTemplate ", "")} - {eventEntity.Username}";
                scenario.ViewId = eventEntity.ViewId;
                scenario = await steamfitterApiClient.UpdateScenarioAsync((Guid)scenario.Id, scenario, ct);
                return scenario;
            }
            catch (Exception ex)
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
            catch(Exception ex)
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
            catch (Exception ex)
            {
                return false;
            }
        }

    }
}


