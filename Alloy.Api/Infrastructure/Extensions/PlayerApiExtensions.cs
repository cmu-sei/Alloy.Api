// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data.Models;
using Alloy.Api.Infrastructure.Exceptions;
using IdentityModel.Client;
using Microsoft.Extensions.Logging;
using Player.Api.Client;

namespace Alloy.Api.Infrastructure.Extensions
{
    public static class PlayerApiExtensions
    {
        public static PlayerApiClient GetPlayerApiClient(IHttpClientFactory httpClientFactory, string apiUrl, TokenResponse tokenResponse)
        {
            var client = ApiClientsExtensions.GetHttpClient(httpClientFactory, apiUrl, tokenResponse);
            var apiClient = new PlayerApiClient(client);
            return apiClient;
        }

        public static async Task<ApiCallResult<Guid>> CreatePlayerViewAsync(PlayerApiClient playerApiClient, EventEntity eventEntity, EventTemplateEntity eventTemplateEntity, List<UserEntity> userList, ILogger logger, CancellationToken ct)
        {
            View clonedView = null;
            try
            {
                var body = new CloneViewCommand()
                {
                    Name = $"{eventTemplateEntity.Name} - {eventEntity.Username}",
                    Description = eventTemplateEntity.Description,
                    IsTemplate = false
                };
                clonedView = await playerApiClient.CloneViewAsync((Guid)eventTemplateEntity.ViewId, body, ct);

                // add user to default team or first non-admin team
                var defaultTeamId = await GetDefaultTeamId(playerApiClient, clonedView, ct);

                await EnsurePlayerUserAsync(playerApiClient, eventEntity.UserId, eventEntity.Username, ct);
                await playerApiClient.AddUserToTeamAsync(defaultTeamId, eventEntity.UserId, ct);

                foreach (var user in userList)
                {
                    if (user.Id != eventEntity.UserId)
                    {
                        await EnsurePlayerUserAsync(playerApiClient, user.Id, user.Name, ct);
                        await playerApiClient.AddUserToTeamAsync(defaultTeamId, user.Id, ct);
                    }
                }

                return ApiCallResult<Guid>.Ok(clonedView.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating the Player View for Event {EventId} from View {TemplateViewId}", eventEntity.Id, eventTemplateEntity.ViewId);

                // Don't leave a half-built View behind for the caller to trip over on the next pass.
                if (clonedView != null)
                {
                    try
                    {
                        await playerApiClient.DeleteViewAsync(clonedView.Id, ct);
                    }
                    catch (Exception deleteEx)
                    {
                        logger.LogError(deleteEx, "Error cleaning up the partially created Player View {ViewId} for Event {EventId}", clonedView.Id, eventEntity.Id);
                    }
                }

                return ex.Classify<Guid>("create the virtual environment");
            }
        }

        public static async Task<ApiCallResult> DeletePlayerViewAsync(Guid? viewId, PlayerApiClient playerApiClient, ILogger logger, CancellationToken ct)
        {
            // no view to delete
            if (viewId == null)
            {
                return ApiCallResult.Ok();
            }
            // try to delete the view
            try
            {
                await playerApiClient.DeleteViewAsync((Guid)viewId, ct);
                return ApiCallResult.Ok();
            }
            catch (Player.Api.Client.ApiException ex) when (
                ex.StatusCode == (int)HttpStatusCode.NotFound ||
                ex.StatusCode == (int)HttpStatusCode.NoContent)
            {
                // there is no View left to delete, so don't hold the Event open retrying
                logger.LogInformation("Player returned {StatusCode} deleting View {ViewId}, which no longer exists. Treating it as deleted.", ex.StatusCode, viewId);
                return ApiCallResult.Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting Player View {ViewId}", viewId);
                return ex.Classify("delete the virtual environment");
            }
        }

        public static async Task<ApiCallResult> AddUserToViewTeamAsync(PlayerApiClient playerApiClient, Guid viewId, Guid userId, ILogger logger, CancellationToken ct)
        {
            try
            {
                var teamId = await GetDefaultTeamId(playerApiClient, viewId, ct);
                await playerApiClient.AddUserToTeamAsync(teamId, userId, ct);
                return ApiCallResult.Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error adding User {UserId} to a Team in Player View {ViewId}", userId, viewId);
                return ex.Classify("add the user to the virtual environment");
            }
        }

        /// <summary>
        /// Player needs a User record before anyone can be put on a Team, and an Event owner may
        /// never have signed in to Player.
        /// </summary>
        private static async Task EnsurePlayerUserAsync(PlayerApiClient playerApiClient, Guid userId, string username, CancellationToken ct)
        {
            try
            {
                await playerApiClient.GetUserAsync(userId, ct);
                return;
            }
            catch (Player.Api.Client.ApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
            {
                // fall through and create the user
            }

            await playerApiClient.CreateUserAsync(
                new CreateUserCommand
                {
                    Id = userId,
                    Name = username
                },
                ct);
        }

        private static async Task<Guid> GetDefaultTeamId(PlayerApiClient playerApiClient, Guid viewId, CancellationToken ct)
        {
            var view = await playerApiClient.GetViewAsync(viewId, ct);
            return await GetDefaultTeamId(playerApiClient, view, ct);
        }

        private static async Task<Guid> GetDefaultTeamId(PlayerApiClient playerApiClient, View view, CancellationToken ct)
        {
            // add user to default team or first non-admin team
            var roles = await playerApiClient.GetTeamRolesAsync(ct);
            var teams = await playerApiClient.GetViewTeamsAsync(view.Id, ct);

            Guid? defaultTeamId = null;

            if (view.DefaultTeamId.HasValue)
            {
                defaultTeamId = teams.Where(x => x.Id == view.DefaultTeamId).FirstOrDefault()?.Id;
            }

            if (!defaultTeamId.HasValue)
            {
                foreach (var team in teams)
                {
                    if (team.Permissions.Where(p => p.Name.Contains("Manage")).Any())
                        continue;

                    if (team.RoleId.HasValue)
                    {
                        var role = roles.Where(r => r.Id == team.RoleId).FirstOrDefault();

                        // Parenthesised deliberately: without the outer group, && binds tighter than
                        // || and a team whose RoleId is not in GetTeamRolesAsync() dereferences null.
                        if (role != null &&
                            (role.AllPermissions || role.Permissions.Where(p => p.Name.Contains("Manage")).Any()))
                            continue;
                    }

                    defaultTeamId = team.Id;
                    break;
                }
            }

            if (!defaultTeamId.HasValue)
                throw new PermanentFailureException(
                    $"Player View {view.Id} has no team that a participant can be added to.");

            return defaultTeamId.Value;
        }
    }
}
