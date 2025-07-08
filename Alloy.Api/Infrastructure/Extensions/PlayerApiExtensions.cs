// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
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

        public static async Task<Guid?> CreatePlayerViewAsync(PlayerApiClient playerApiClient, EventEntity eventEntity, EventTemplateEntity eventTemplateEntity, List<UserEntity> userList, CancellationToken ct)
        {
            View clonedView = null;
            try
            {
                var body = new CloneViewCommand()
                {
                    Name = $"{eventTemplateEntity.Name} - {eventEntity.Username}",
                    Description = eventTemplateEntity.Description
                };
                clonedView = await playerApiClient.CloneViewAsync((Guid)eventTemplateEntity.ViewId, body, ct);

                // add user to first non-admin team
                var roles = await playerApiClient.GetRolesAsync(ct);
                var teams = await playerApiClient.GetViewTeamsAsync(clonedView.Id, ct);

                foreach (var team in teams)
                {
                    if (team.Permissions.Where(p => p.Name.Contains("Manage")).Any())
                        continue;

                    if (team.RoleId.HasValue)
                    {
                        var role = roles.Where(r => r.Id == team.RoleId).FirstOrDefault();

                        if (role != null && role.Permissions.Where(p => p.Name.Contains("Manage")).Any())
                            continue;
                    }

                    try
                    {
                        var owner = await playerApiClient.GetUserAsync(eventEntity.UserId, ct);
                    }
                    catch (Exception)
                    {
                        await playerApiClient.CreateUserAsync(
                            new CreateUserCommand
                            {
                                Id = eventEntity.UserId,
                                Name = eventEntity.Username
                            });
                    }

                    await playerApiClient.AddUserToTeamAsync(team.Id, eventEntity.UserId, ct);

                    foreach (var user in userList)
                    {
                        if (user.Id != eventEntity.UserId)
                        {
                            try
                            {
                                var playerUser = await playerApiClient.GetUserAsync(user.Id, ct);
                            }
                            catch (Exception)
                            {
                                await playerApiClient.CreateUserAsync(
                                    new CreateUserCommand
                                    {
                                        Id = user.Id,
                                        Name = user.Name
                                    });
                            }

                            await playerApiClient.AddUserToTeamAsync(team.Id, user.Id, ct);
                        }
                    }
                }

                return clonedView.Id;
            }
            catch (Exception)
            {
                try
                {
                    if (clonedView != null)
                    {
                        await playerApiClient.DeleteViewAsync(clonedView.Id);
                    }
                }
                catch (Exception)
                {
                    return null;
                }

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
            catch (Exception)
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
                    if (team.Permissions.Where(p => p.Name.Contains("Manage")).Any())
                        continue;

                    if (team.RoleId.HasValue)
                    {
                        var role = roles.Where(r => r.Id == team.RoleId).FirstOrDefault();

                        if (role != null && role.Permissions.Where(p => p.Name.Contains("Manage")).Any())
                            continue;
                    }

                    await playerApiClient.AddUserToTeamAsync(team.Id, userId, ct);
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
