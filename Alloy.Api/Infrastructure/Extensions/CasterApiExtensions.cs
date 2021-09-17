// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IdentityModel.Client;
using Caster.Api;
using Caster.Api.Client;
using Player.Api;
using Player.Api.Models;
using Alloy.Api.Data.Models;
using Microsoft.Extensions.Logging;
using System.Net;

namespace Alloy.Api.Infrastructure.Extensions
{
    public static class CasterApiExtensions
    {
        public static CasterApiClient GetCasterApiClient(IHttpClientFactory httpClientFactory, string apiUrl, TokenResponse tokenResponse)
        {
            var client = ApiClientsExtensions.GetHttpClient(httpClientFactory, apiUrl, tokenResponse);
            var apiClient = new CasterApiClient(client);
            return apiClient;
        }

        public static async Task<Guid?> CreateCasterWorkspaceAsync(CasterApiClient casterApiClient, EventEntity eventEntity, Guid directoryId, string varsFileContent, bool useDynamicHost, CancellationToken ct)
        {
            try
            {
                // remove special characters from the user name, use lower case and replace spaces with underscores
                var userName = Regex.Replace(eventEntity.Username.ToLower().Replace(" ", "_"), "[@&'(\\s)<>#]", "", RegexOptions.None);
                // create the new workspace
                var workspaceCommand = new CreateWorkspaceCommand()
                {
                    Name = $"{userName}-{eventEntity.UserId.ToString()}",
                    DirectoryId = directoryId,
                    DynamicHost = useDynamicHost
                };
                var workspaceId = (await casterApiClient.CreateWorkspaceAsync(workspaceCommand, ct)).Id;
                // create the workspace variable file
                var createFileCommand = new CreateFileCommand()
                {
                    Name = $"{workspaceCommand.Name}.auto.tfvars",
                    DirectoryId = directoryId,
                    WorkspaceId = workspaceId,
                    Content = varsFileContent
                };
                await casterApiClient.CreateFileAsync(createFileCommand, ct);
                return workspaceId;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public static async Task<string> GetCasterVarsFileContentAsync(EventEntity eventEntity, PlayerApiClient playerApiClient, CancellationToken ct)
        {
            try
            {
                var varsFileContent = "";
                var view = await playerApiClient.GetViewAsync((Guid)eventEntity.ViewId, ct);

                // TODO: exercise_id is deprecated. Remove when no longer in use
                varsFileContent = $"exercise_id = \"{view.Id}\"\r\nview_id = \"{view.Id}\"\r\nuser_id = \"{eventEntity.UserId}\"\r\nusername = \"{eventEntity.Username}\"\r\n";
                var teams = await playerApiClient.GetViewTeamsAsync((Guid)view.Id, ct);

                foreach (var team in teams)
                {
                    var cleanTeamName = Regex.Replace(team.Name.ToLower().Replace(" ", "_"), "[@&'(\\s)<>#]", "", RegexOptions.None);
                    varsFileContent += $"{cleanTeamName} = \"{team.Id}\"\r\n";
                }

                return varsFileContent;
            }
            catch (Exception ex)
            {
                return "";
            }
        }

        public static async Task<Guid?> CreateRunAsync(
            EventEntity eventEntity,
            CasterApiClient casterApiClient,
            bool isDestroy,
            ILogger logger,
            CancellationToken ct)
        {
            var runCommand = new CreateRunCommand()
            {
                WorkspaceId = eventEntity.WorkspaceId.Value,
                IsDestroy = isDestroy
            };
            try
            {
                var casterRun = await casterApiClient.CreateRunAsync(runCommand, ct);
                return casterRun.Id;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error Creating Run");
                return null;
            }
        }

        public static async Task<bool> WaitForRunToBePlannedAsync(
            EventEntity eventEntity,
            CasterApiClient casterApiClient,
            int loopIntervalSeconds,
            int maxWaitMinutes,
            ILogger logger,
            CancellationToken ct)
        {
            if (eventEntity.RunId == null)
            {
                return false;
            }
            var endTime = DateTime.UtcNow.AddMinutes(maxWaitMinutes);
            var status = RunStatus.Planning;

            while (status == RunStatus.Planning && DateTime.UtcNow < endTime)
            {
                var casterRun = await casterApiClient.GetRunAsync((Guid)eventEntity.RunId, false, false);
                status = casterRun.Status;
                // if not there yet, pause before the next check
                if (status == RunStatus.Planning)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(loopIntervalSeconds));
                }
            }
            if (status == RunStatus.Planned)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static async Task<bool> ApplyRunAsync(
            EventEntity eventEntity,
            CasterApiClient casterApiClient,
            CancellationToken ct)
        {
            var initialInternalStatus = eventEntity.InternalStatus;
            // if status is Planned or Applying
            try
            {
                await casterApiClient.ApplyRunAsync((Guid)eventEntity.RunId, ct);
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public static async Task<bool> DeleteCasterWorkspaceAsync(EventEntity eventEntity,
            CasterApiClient casterApiClient, TokenResponse tokenResponse, CancellationToken ct)
        {
            try
            {
                await casterApiClient.DeleteWorkspaceAsync((Guid)eventEntity.WorkspaceId, ct);
                return true;
            }
            catch (ApiException ex)
            {
                if (ex.StatusCode == (int)HttpStatusCode.NotFound)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static async Task<bool> WaitForRunToBeAppliedAsync(
            EventEntity eventEntity,
            CasterApiClient casterApiClient,
            int loopIntervalSeconds,
            int maxWaitMinutes,
            ILogger logger,
            CancellationToken ct)
        {
            if (eventEntity.RunId == null)
            {
                return false;
            }
            var endTime = DateTime.UtcNow.AddMinutes(maxWaitMinutes);
            var status = RunStatus.Applying;

            while ((status == RunStatus.Applying ||
                    status == RunStatus.Planned ||
                    status == RunStatus.Queued ||
                    status == RunStatus.Applied__State_Error ||
                    status == RunStatus.Failed__State_Error)
                    && DateTime.UtcNow < endTime)
            {
                var casterRun = await casterApiClient.GetRunAsync((Guid)eventEntity.RunId, false, false);
                status = casterRun.Status;

                // if not there yet, pause before the next check
                if (status == RunStatus.Applying ||
                    status == RunStatus.Planned ||
                    status == RunStatus.Queued ||
                    status == RunStatus.Applied__State_Error ||
                    status == RunStatus.Failed__State_Error)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(loopIntervalSeconds));

                    if (status == RunStatus.Applied__State_Error ||
                        status == RunStatus.Failed__State_Error)
                    {
                        await casterApiClient.SaveStateAsync(eventEntity.RunId.Value);
                    }
                }
            }
            if (status == RunStatus.Applied)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

    }
}
