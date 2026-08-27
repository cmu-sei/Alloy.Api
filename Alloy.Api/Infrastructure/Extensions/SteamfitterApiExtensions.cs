// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IdentityModel.Client;
using Alloy.Api.Data.Models;
using Microsoft.Extensions.Logging;
using Steamfitter.Api.Client;
using System.Collections.Generic;
using System.Net;

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

        /// <summary>
        /// Clones a ScenarioTemplate into a new Scenario for the owner of the given Event.
        /// </summary>
        /// <exception cref="Exception">
        /// Thrown when Steamfitter rejects the request, carrying the status code and the response
        /// body Steamfitter returned. Any other failure (an unreachable API, an expired token) is
        /// allowed to propagate as-is.
        /// </exception>
        public static async Task<Scenario> CreateSteamfitterScenarioAsync(SteamfitterApiClient steamfitterApiClient, EventEntity eventEntity, Guid scenarioTemplateId, CancellationToken ct)
        {
            // Send the name along with the id so Steamfitter can create a User record for an
            // event owner who has never signed in to Steamfitter, and give them a Membership
            // on the new Scenario.
            var options = new ScenarioCloneOptions
            {
                ViewId = eventEntity.ViewId,
                NameSuffix = $"- {eventEntity.Username}",
                Users = new List<ScenarioCloneUser>()
                {
                    new ScenarioCloneUser()
                    {
                        Id = eventEntity.UserId,
                        Name = eventEntity.Username
                    }
                }
            };

            try
            {
                return await steamfitterApiClient.CreateScenarioFromScenarioTemplateAsync(scenarioTemplateId, options, ct);
            }
            catch (ApiException ex)
            {
                // ApiException truncates the response body in its own Message, so spell out the
                // whole thing here along with the Alloy side of the request.
                throw new Exception(
                    $"Steamfitter returned {ex.StatusCode} creating a Scenario from ScenarioTemplate {scenarioTemplateId} " +
                    $"for Event {eventEntity.Id}, user {eventEntity.Username} ({eventEntity.UserId}). Response: {ex.Response}",
                    ex);
            }
        }

        /// <summary>
        /// Starts the given Scenario. Returns false, after logging why, if the Scenario could not be
        /// started, since the caller treats that as "not done yet" and retries.
        /// </summary>
        public static async Task<bool> StartSteamfitterScenarioAsync(SteamfitterApiClient steamfitterApiClient, Guid scenarioId, ILogger logger, CancellationToken ct)
        {
            try
            {
                await steamfitterApiClient.StartScenarioAsync(scenarioId, ct);
                return true;
            }
            catch (ApiException ex)
            {
                // ApiException truncates the response body in its own Message, so spell out the whole thing
                logger.LogError(ex, $"Steamfitter returned {ex.StatusCode} starting Scenario {scenarioId}. Response: {ex.Response}");
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error starting Steamfitter Scenario {scenarioId}.");
                return false;
            }
        }

        /// <summary>
        /// Ends the given Scenario. A Scenario that is already gone from Steamfitter counts as ended.
        /// Returns false, after logging why, if the Scenario could not be ended, since the caller
        /// treats that as "not done yet" and retries.
        /// </summary>
        public static async Task<bool> EndSteamfitterScenarioAsync(Guid? scenarioId, SteamfitterApiClient steamfitterApiClient, ILogger logger, CancellationToken ct)
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
            catch (ApiException ex)
            {
                // there is no Scenario left to end, so don't hold the Event open retrying
                if (ex.StatusCode == (int)HttpStatusCode.NotFound || ex.StatusCode == (int)HttpStatusCode.NoContent)
                {
                    logger.LogInformation($"Steamfitter returned {ex.StatusCode} ending Scenario {scenarioId}, which no longer exists. Treating it as ended.");
                    return true;
                }

                // ApiException truncates the response body in its own Message, so spell out the whole thing
                logger.LogError(ex, $"Steamfitter returned {ex.StatusCode} ending Scenario {scenarioId}. Response: {ex.Response}");
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error ending Steamfitter Scenario {scenarioId}.");
                return false;
            }
        }

    }
}
