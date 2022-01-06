// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data.Models;
using IdentityModel.Client;
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

        public static async Task<Guid?> CreatePlayerViewAsync(PlayerApiClient playerApiClient, EventEntity eventEntity, Guid parentViewId, CancellationToken ct)
        {
            try
            {
                var view = await playerApiClient.CloneViewAsync(parentViewId, ct);
                view.Name = $"{view.Name.Replace("Clone of ", "")} - {eventEntity.Username}";
                await playerApiClient.UpdateViewAsync((Guid)view.Id, view, ct);
                // add user to first non-admin team
                var roles = await playerApiClient.GetRolesAsync(ct);
                var teams = (await playerApiClient.GetViewTeamsAsync((Guid)view.Id, ct));
                foreach (var team in teams)
                {
                    if (team.Permissions.Where(p => p.Key == "ViewAdmin").Any())
                        continue;

                    if (team.RoleId.HasValue)
                    {
                        var role = roles.Where(r => r.Id == team.RoleId).FirstOrDefault();

                        if (role != null && role.Permissions.Where(p => p.Key == "ViewAdmin").Any())
                            continue;
                    }

                    await playerApiClient.AddUserToTeamAsync(team.Id, eventEntity.UserId, ct);
                }
                return view.Id;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public static async Task<bool> DeletePlayerViewAsync(string playerApiUrl, Guid? viewId, PlayerApiClient playerApiClient, CancellationToken ct)
        {
            // no view to delete
            if (viewId == null)
            {
                return true;
            }
            // try to delete the view
            try
            {
                await playerApiClient.DeleteViewAsync((Guid)viewId, ct);
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public static async Task<bool> AddUserToViewTeamAsync(PlayerApiClient playerApiClient, Guid viewId, Guid userId, CancellationToken ct)
        {
            try
            {
                var view = await playerApiClient.GetViewAsync(viewId, ct);
                var roles = await playerApiClient.GetRolesAsync(ct);
                var teams = await playerApiClient.GetViewTeamsAsync(viewId, ct);
                //Get first non-admin team
                foreach (var team in teams)
                {
                    if (team.Permissions.Where(p => p.Key == "ViewAdmin").Any())
                        continue;

                    if (team.RoleId.HasValue)
                    {
                        var role = roles.Where(r => r.Id == team.RoleId).FirstOrDefault();

                        if (role != null && role.Permissions.Where(p => p.Key == "ViewAdmin").Any())
                            continue;
                    }

                    await playerApiClient.AddUserToTeamAsync(team.Id, userId, ct);
                }

                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
    }
}
