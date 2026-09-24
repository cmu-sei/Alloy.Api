// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data.Models;
using IdentityModel.Client;
using Microsoft.Extensions.Logging;
using Steamfitter.Api.Client;

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
        public static async Task<ApiCallResult<Scenario>> CreateSteamfitterScenarioAsync(SteamfitterApiClient steamfitterApiClient, EventEntity eventEntity, Guid scenarioTemplateId, ILogger logger, CancellationToken ct)
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
                var scenario = await steamfitterApiClient.CreateScenarioFromScenarioTemplateAsync(scenarioTemplateId, options, ct);
                return ApiCallResult<Scenario>.Ok(scenario);
            }
            catch (ApiException ex)
            {
                // ApiException truncates the response body in its own Message, so spell out the whole thing
                logger.LogError(ex, "Steamfitter returned {StatusCode} creating a Scenario from ScenarioTemplate {ScenarioTemplateId} for Event {EventId}. Response: {Response}", ex.StatusCode, scenarioTemplateId, eventEntity.Id, ex.Response);
                return ex.Classify<Scenario>("create the scenario");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating a Steamfitter Scenario from ScenarioTemplate {ScenarioTemplateId} for Event {EventId}", scenarioTemplateId, eventEntity.Id);
                return ex.Classify<Scenario>("create the scenario");
            }
        }

        /// <summary>
        /// Starts the given Scenario.
        /// </summary>
        public static async Task<ApiCallResult> StartSteamfitterScenarioAsync(SteamfitterApiClient steamfitterApiClient, Guid scenarioId, ILogger logger, CancellationToken ct)
        {
            try
            {
                await steamfitterApiClient.StartScenarioAsync(scenarioId, ct);
                return ApiCallResult.Ok();
            }
            catch (ApiException ex)
            {
                // ApiException truncates the response body in its own Message, so spell out the whole thing
                logger.LogError(ex, "Steamfitter returned {StatusCode} starting Scenario {ScenarioId}. Response: {Response}", ex.StatusCode, scenarioId, ex.Response);
                return ex.Classify("start the scenario");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error starting Steamfitter Scenario {ScenarioId}.", scenarioId);
                return ex.Classify("start the scenario");
            }
        }

        /// <summary>
        /// Ends the given Scenario. A Scenario that is already gone from Steamfitter counts as ended.
        /// </summary>
        public static async Task<ApiCallResult> EndSteamfitterScenarioAsync(Guid? scenarioId, SteamfitterApiClient steamfitterApiClient, ILogger logger, CancellationToken ct)
        {
            // no scenario to end
            if (scenarioId == null)
            {
                return ApiCallResult.Ok();
            }
            try
            {
                await steamfitterApiClient.EndScenarioAsync((Guid)scenarioId, ct);
                return ApiCallResult.Ok();
            }
            catch (ApiException ex) when (
                ex.StatusCode == (int)HttpStatusCode.NotFound ||
                ex.StatusCode == (int)HttpStatusCode.NoContent)
            {
                // there is no Scenario left to end, so don't hold the Event open retrying
                logger.LogInformation("Steamfitter returned {StatusCode} ending Scenario {ScenarioId}, which no longer exists. Treating it as ended.", ex.StatusCode, scenarioId);
                return ApiCallResult.Ok();
            }
            catch (ApiException ex)
            {
                // ApiException truncates the response body in its own Message, so spell out the whole thing
                logger.LogError(ex, "Steamfitter returned {StatusCode} ending Scenario {ScenarioId}. Response: {Response}", ex.StatusCode, scenarioId, ex.Response);
                return ex.Classify("end the scenario");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error ending Steamfitter Scenario {ScenarioId}.", scenarioId);
                return ex.Classify("end the scenario");
            }
        }
    }
}
