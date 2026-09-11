// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data.Models;
using Caster.Api.Client;
using IdentityModel.Client;
using Microsoft.Extensions.Logging;
using Player.Api.Client;

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

        public static async Task<ApiCallResult<Guid>> CreateCasterWorkspaceAsync(CasterApiClient casterApiClient, EventEntity eventEntity, Guid directoryId, string varsFileContent, bool useDynamicHost, ILogger logger, CancellationToken ct)
        {
            Guid? createdWorkspaceId = null;
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
                createdWorkspaceId = workspaceId;
                // create the workspace variable file
                var createFileCommand = new CreateFileCommand()
                {
                    Name = $"{workspaceCommand.Name}.auto.tfvars",
                    DirectoryId = directoryId,
                    WorkspaceId = workspaceId,
                    Content = varsFileContent
                };
                await casterApiClient.CreateFileAsync(createFileCommand, ct);
                return ApiCallResult<Guid>.Ok(workspaceId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating the Caster workspace for Event {EventId} in Directory {DirectoryId}", eventEntity.Id, directoryId);

                // The Workspace id never reaches the Event, so nothing would ever clean this one up.
                // Drop it now rather than leave an orphan behind for every attempt.
                if (createdWorkspaceId.HasValue)
                {
                    try
                    {
                        await casterApiClient.DeleteWorkspaceAsync(createdWorkspaceId.Value, ct);
                    }
                    catch (Exception deleteEx)
                    {
                        logger.LogError(deleteEx, "Error cleaning up the partially created Caster Workspace {WorkspaceId} for Event {EventId}", createdWorkspaceId, eventEntity.Id);
                    }
                }

                return ex.Classify<Guid>("prepare the infrastructure workspace");
            }
        }

        public static async Task<ApiCallResult<string>> GetCasterVarsFileContentAsync(EventEntity eventEntity, PlayerApiClient playerApiClient, ILogger logger, CancellationToken ct)
        {
            try
            {
                var view = await playerApiClient.GetViewAsync((Guid)eventEntity.ViewId, ct);

                // TODO: exercise_id is deprecated. Remove when no longer in use
                var varsFileContent = $"exercise_id = \"{view.Id}\"\r\nview_id = \"{view.Id}\"\r\nuser_id = \"{eventEntity.UserId}\"\r\nusername = \"{eventEntity.Username}\"\r\n";
                var teams = await playerApiClient.GetViewTeamsAsync((Guid)view.Id, ct);

                foreach (var team in teams)
                {
                    var cleanTeamName = Regex.Replace(team.Name.ToLower().Replace(" ", "_"), "[@&'(\\s)<>#]", "", RegexOptions.None);
                    varsFileContent += $"{cleanTeamName} = \"{team.Id}\"\r\n";
                }

                return ApiCallResult<string>.Ok(varsFileContent);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error building the Caster variables file for Event {EventId} from View {ViewId}", eventEntity.Id, eventEntity.ViewId);
                return ex.Classify<string>("read the virtual environment configuration");
            }
        }

        public static async Task<ApiCallResult<Guid>> CreateRunAsync(
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
                // A previous create may have succeeded even if its response was lost.
                var pending = (await casterApiClient.GetRunsByWorkspaceIdAsync(
                    runCommand.WorkspaceId, null, false, false, ct))
                    .FirstOrDefault(x => !IsTerminal(x.Status));
                if (pending != null)
                {
                    return pending.IsDestroy == isDestroy
                        ? ApiCallResult<Guid>.Ok(pending.Id)
                        : ApiCallResult<Guid>.Transient("Another infrastructure run must finish before this operation can start.");
                }
                var casterRun = await casterApiClient.CreateRunAsync(runCommand, ct);
                return ApiCallResult<Guid>.Ok(casterRun.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating a Caster run for Event {EventId} in Workspace {WorkspaceId} (isDestroy: {IsDestroy})", eventEntity.Id, eventEntity.WorkspaceId, isDestroy);
                if (ex is Caster.Api.Client.ApiException { StatusCode: 409 })
                {
                    return ApiCallResult<Guid>.Transient("The infrastructure workspace is busy; checking its current run again.", ex.ToString());
                }
                return ex.Classify<Guid>(isDestroy
                    ? "start tearing down the infrastructure"
                    : "start building the infrastructure");
            }
        }

        public static async Task<ApiCallResult> WaitForRunToBePlannedAsync(
            EventEntity eventEntity,
            CasterApiClient casterApiClient,
            int loopIntervalSeconds,
            int maxWaitMinutes,
            bool isDestroy,
            ILogger logger,
            CancellationToken ct,
            Func<Task<bool>> endRequested = null)
        {
            if (eventEntity.RunId == null)
            {
                return ApiCallResult.Permanent("The infrastructure run is missing and cannot be planned.");
            }
            var endTime = DateTime.UtcNow.AddMinutes(maxWaitMinutes);
            var status = RunStatus.Planning;

            while ((status == RunStatus.Queued || status == RunStatus.Planning) && DateTime.UtcNow < endTime)
            {
                if (!isDestroy && endRequested != null && await endRequested())
                    return ApiCallResult.EndRequested();
                try
                {
                    // the plan output is deliberately not requested here: this loop can poll for
                    // many minutes, and dragging the whole plan down on every pass is a real load
                    // problem for Caster. It is fetched once below, only if the run failed.
                    var casterRun = await casterApiClient.GetRunAsync((Guid)eventEntity.RunId, false, false, ct);
                    status = casterRun.Status;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error reading Caster run {RunId} while waiting for it to be planned", eventEntity.RunId);
                    return ex.Classify("plan the infrastructure");
                }

                // if not there yet, pause before the next check
                if (status == RunStatus.Planning || status == RunStatus.Queued)
                {
                    await Task.Delay(TimeSpan.FromSeconds(loopIntervalSeconds), ct);
                }
            }

            if (!isDestroy && endRequested != null && await endRequested())
                return ApiCallResult.EndRequested();

            // An apply may already exist after recovery of an uncertain operation.
            if (status == RunStatus.Planned || status == RunStatus.ApplyQueued ||
                status == RunStatus.Applying || status == RunStatus.Applied ||
                status == RunStatus.Applied__State_Error || status == RunStatus.Failed__State_Error)
            {
                return ApiCallResult.Ok();
            }

            if (status == RunStatus.Failed || status == RunStatus.Rejected)
            {
                // Now, and only now, pull the plan output so there is something to show for it.
                var output = await GetPlanOutputAsync(eventEntity, casterApiClient, logger, ct);
                logger.LogError("Caster run {RunId} for Event {EventId} ended planning with status {Status}. Output: {Output}", eventEntity.RunId, eventEntity.Id, status, output);
                return ApiCallResult.Permanent(
                    isDestroy
                        ? "Infrastructure teardown failed while planning the removal."
                        : "Infrastructure deployment failed while planning the changes.",
                    output);
            }

            // Still queued or planning means the wait simply ran out; any other status means Caster
            // moved the run somewhere unexpected. Either way it is worth another pass - the caller's
            // retry ceiling is what guarantees this terminates.
            logger.LogWarning("Caster run {RunId} for Event {EventId} did not reach Planned within {MaxWaitMinutes} minutes; last status was {Status}", eventEntity.RunId, eventEntity.Id, maxWaitMinutes, status);
            return ApiCallResult.Transient(
                "Infrastructure planning is taking longer than expected; retrying.",
                $"Run {eventEntity.RunId} did not reach Planned within {maxWaitMinutes} minutes. Last status: {status}");
        }

        public static async Task<ApiCallResult> ApplyRunAsync(
            EventEntity eventEntity,
            CasterApiClient casterApiClient,
            ILogger logger,
            CancellationToken ct)
        {
            try
            {
                var run = await casterApiClient.GetRunAsync((Guid)eventEntity.RunId, false, false, ct);
                if (run.ApplyId != null || run.Status == RunStatus.ApplyQueued ||
                    run.Status == RunStatus.Applying || run.Status == RunStatus.Applied ||
                    run.Status == RunStatus.Applied__State_Error || run.Status == RunStatus.Failed__State_Error)
                {
                    // The apply was accepted previously. Observe its result instead of
                    // submitting it twice and interpreting the resulting 409 as failure.
                    return ApiCallResult.Ok();
                }
                await casterApiClient.ApplyRunAsync((Guid)eventEntity.RunId, ct);
                return ApiCallResult.Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error applying Caster run {RunId} for Event {EventId}", eventEntity.RunId, eventEntity.Id);
                if (ex is Caster.Api.Client.ApiException { StatusCode: 409 })
                {
                    return ApiCallResult.Transient("The infrastructure run changed; checking its current apply again.", ex.ToString());
                }
                return ex.Classify("apply the infrastructure changes");
            }
        }

        public static async Task<ApiCallResult> DeleteCasterWorkspaceAsync(EventEntity eventEntity,
            CasterApiClient casterApiClient, ILogger logger, CancellationToken ct)
        {
            // no workspace to delete
            if (eventEntity.WorkspaceId == null)
            {
                return ApiCallResult.Ok();
            }
            try
            {
                await casterApiClient.DeleteWorkspaceAsync((Guid)eventEntity.WorkspaceId, ct);
                return ApiCallResult.Ok();
            }
            catch (Caster.Api.Client.ApiException ex) when (
                ex.StatusCode == (int)HttpStatusCode.NotFound ||
                ex.StatusCode == (int)HttpStatusCode.NoContent)
            {
                // there is no Workspace left to delete, so don't hold the Event open retrying
                logger.LogInformation("Caster returned {StatusCode} deleting Workspace {WorkspaceId}, which no longer exists. Treating it as deleted.", ex.StatusCode, eventEntity.WorkspaceId);
                return ApiCallResult.Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting Caster Workspace {WorkspaceId} for Event {EventId}", eventEntity.WorkspaceId, eventEntity.Id);
                return ex.Classify("delete the infrastructure workspace");
            }
        }

        public static async Task<ApiCallResult> WaitForRunToBeAppliedAsync(
            EventEntity eventEntity,
            CasterApiClient casterApiClient,
            int loopIntervalSeconds,
            int maxWaitMinutes,
            bool isDestroy,
            ILogger logger,
            CancellationToken ct,
            Func<Task<bool>> endRequested = null)
        {
            if (eventEntity.RunId == null)
            {
                return ApiCallResult.Permanent("The infrastructure run is missing and cannot be applied.");
            }
            var endTime = DateTime.UtcNow.AddMinutes(maxWaitMinutes);
            var status = RunStatus.Applying;

            while (IsStillWorking(status) && DateTime.UtcNow < endTime)
            {
                if (!isDestroy && endRequested != null && await endRequested())
                    return ApiCallResult.EndRequested();
                try
                {
                    // the apply output is deliberately not requested here: this loop can run for
                    // hours, and dragging the whole apply log down on every poll is a real load
                    // problem for Caster. It is fetched once below, only if the run failed.
                    var casterRun = await casterApiClient.GetRunAsync((Guid)eventEntity.RunId, false, false, ct);
                    status = casterRun.Status;

                    // if not there yet, pause before the next check
                    if (IsStillWorking(status))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(loopIntervalSeconds), ct);

                        if (status == RunStatus.Applied__State_Error ||
                            status == RunStatus.Failed__State_Error)
                        {
                            try
                            {
                                await casterApiClient.SaveStateAsync(eventEntity.RunId.Value, ct);
                            }
                            catch (Caster.Api.Client.ApiException ex) when (ex.StatusCode == 409)
                            {
                                // The workspace may be locked, or another caller already saved the state.
                                // Recheck this run using the caller's existing retry budget.
                                logger.LogDebug(ex, "Conflict saving state for Caster run {RunId}; rechecking the run.", eventEntity.RunId);
                                return ApiCallResult.Transient(
                                    "The infrastructure state could not be saved yet; checking the run again.", ex.ToString());
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error reading Caster run {RunId} while waiting for it to be applied", eventEntity.RunId);
                    return ex.Classify(isDestroy
                        ? "tear down the infrastructure"
                        : "build the infrastructure");
                }
            }

            if (!isDestroy && endRequested != null && await endRequested())
                return ApiCallResult.EndRequested();

            if (status == RunStatus.Applied)
            {
                return ApiCallResult.Ok();
            }

            if (status == RunStatus.Failed || status == RunStatus.Rejected)
            {
                // Now, and only now, pull the apply output so there is something to show for it.
                var output = await GetApplyOutputAsync(eventEntity, casterApiClient, logger, ct);
                logger.LogError("Caster run {RunId} for Event {EventId} ended with status {Status}. Output: {Output}", eventEntity.RunId, eventEntity.Id, status, output);
                return ApiCallResult.Permanent(
                    isDestroy
                        ? "Infrastructure teardown failed while removing the virtual environment."
                        : "Infrastructure deployment failed while building the virtual environment.",
                    output);
            }

            logger.LogWarning("Caster run {RunId} for Event {EventId} did not reach Applied within {MaxWaitMinutes} minutes; last status was {Status}", eventEntity.RunId, eventEntity.Id, maxWaitMinutes, status);
            return ApiCallResult.Transient(
                isDestroy
                    ? "Removing the virtual environment is taking longer than expected; retrying."
                    : "Building the virtual environment is taking longer than expected; retrying.",
                $"Run {eventEntity.RunId} did not reach Applied within {maxWaitMinutes} minutes. Last status: {status}");
        }

        /// <summary>
        /// Statuses that mean the apply has not settled one way or the other yet.
        /// </summary>
        private static bool IsStillWorking(RunStatus status)
        {
            return status == RunStatus.Applying ||
                   status == RunStatus.ApplyQueued ||
                   status == RunStatus.Planned ||
                   status == RunStatus.Queued ||
                   status == RunStatus.Applied__State_Error ||
                   status == RunStatus.Failed__State_Error;
        }

        internal static bool IsTerminal(RunStatus status) =>
            status == RunStatus.Applied || status == RunStatus.Failed || status == RunStatus.Rejected;

        /// <summary>
        /// Settle launch runs before teardown. A returned destroy run should be resumed,
        /// never cancelled. The caller retains the deadline across transient API retries.
        /// </summary>
        internal static async Task<ApiCallResult<Run>> SettleLaunchRunsAsync(
            EventEntity eventEntity, CasterApiClient client, int intervalSeconds,
            DateTime deadline, ILogger logger, CancellationToken ct)
        {
            if (eventEntity.WorkspaceId == null)
                return ApiCallResult<Run>.Ok(null);

            var cancellationSent = new HashSet<Guid>();
            try
            {
                while (true)
                {
                    var pending = (await client.GetRunsByWorkspaceIdAsync(
                        eventEntity.WorkspaceId.Value, null, false, false, ct))
                        .Where(x => !IsTerminal(x.Status)).ToList();
                    var launchRuns = pending.Where(x => !x.IsDestroy).ToList();
                    if (launchRuns.Count == 0)
                        return ApiCallResult<Run>.Ok(pending.FirstOrDefault());

                    if (DateTime.UtcNow >= deadline)
                        return ApiCallResult<Run>.Permanent(
                            "The infrastructure run did not stop in time. Cleanup can be retried by an administrator.",
                            $"Workspace {eventEntity.WorkspaceId} still has unfinished launch runs: {string.Join(", ", launchRuns.Select(x => x.Id))}.");

                    foreach (var run in launchRuns)
                    {
                        try
                        {
                            switch (run.Status)
                            {
                                case RunStatus.Planned:
                                    await client.RejectRunAsync(run.Id, ct);
                                    break;
                                case RunStatus.Queued:
                                case RunStatus.Planning:
                                case RunStatus.ApplyQueued:
                                case RunStatus.Applying:
                                    if (cancellationSent.Add(run.Id))
                                        await client.CancelRunAsync(run.Id, new CancelRunCommand { Force = false }, ct);
                                    break;
                                case RunStatus.Applied__State_Error:
                                case RunStatus.Failed__State_Error:
                                    await client.SaveStateAsync(run.Id, ct);
                                    break;
                            }
                        }
                        catch (Caster.Api.Client.ApiException ex) when (
                            ex.StatusCode == 400 || ex.StatusCode == 404 || ex.StatusCode == 409)
                        {
                            // Completion can race cancellation/rejection. Re-read rather
                            // than inferring success or treating the race as a launch failure.
                            cancellationSent.Remove(run.Id);
                            logger.LogDebug(ex, "Caster run {RunId} changed while settling it.", run.Id);
                        }
                    }
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not settle launch runs for Event {EventId}.", eventEntity.Id);
                // A missing workspace must be confirmed by the caller's resource/deletion
                // checks. Other failures use the existing end retry budget.
                if (ex is Caster.Api.Client.ApiException { StatusCode: 404 })
                    return ApiCallResult<Run>.Ok(null);
                return ex.Classify<Run>("stop the infrastructure run");
            }
        }

        private static async Task<string> GetPlanOutputAsync(
            EventEntity eventEntity,
            CasterApiClient casterApiClient,
            ILogger logger,
            CancellationToken ct)
        {
            try
            {
                var casterRun = await casterApiClient.GetRunAsync((Guid)eventEntity.RunId, true, false, ct);
                return casterRun?.Plan?.Output ?? "No output available";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error reading the plan output for Caster run {RunId}", eventEntity.RunId);
                return "The output of the failed run could not be retrieved.";
            }
        }

        private static async Task<string> GetApplyOutputAsync(
            EventEntity eventEntity,
            CasterApiClient casterApiClient,
            ILogger logger,
            CancellationToken ct)
        {
            try
            {
                var casterRun = await casterApiClient.GetRunAsync((Guid)eventEntity.RunId, false, true, ct);
                return casterRun?.Apply?.Output ?? "No output available";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error reading the apply output for Caster run {RunId}", eventEntity.RunId);
                return "The output of the failed run could not be retrieved.";
            }
        }

        /// <summary>
        /// How many resources Caster still holds in the Event's Workspace.
        /// <para>
        /// A Workspace that Caster no longer knows about counts as empty. There is nothing left to
        /// destroy, and retrying a 404 until the teardown ceiling would strand the Event in Failed
        /// with a WorkspaceId that can never be cleared.
        /// </para>
        /// </summary>
        public static async Task<ApiCallResult<int>> GetWorkspaceResourceCountAsync(
            EventEntity eventEntity,
            CasterApiClient casterApiClient,
            ILogger logger,
            CancellationToken ct)
        {
            if (!eventEntity.WorkspaceId.HasValue)
            {
                return ApiCallResult<int>.Ok(0);
            }

            try
            {
                var resources = await casterApiClient.GetResourcesByWorkspaceAsync(eventEntity.WorkspaceId.Value, ct);
                return ApiCallResult<int>.Ok(resources.Count);
            }
            catch (Caster.Api.Client.ApiException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
            {
                logger.LogInformation("Caster no longer has Workspace {WorkspaceId} for Event {EventId}. Treating it as empty.", eventEntity.WorkspaceId, eventEntity.Id);
                return ApiCallResult<int>.Ok(0);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error checking Resources for Workspace {WorkspaceId} in Event {EventId}", eventEntity.WorkspaceId, eventEntity.Id);
                return ex.Classify<int>("check the infrastructure workspace");
            }
        }
    }
}
