// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Infrastructure;
using Alloy.Api.Infrastructure.Extensions;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using IdentityModel.Client;
using Steamfitter.Api.Client;
using Task = System.Threading.Tasks.Task;
using Player.Api.Client;
using Caster.Api.Client;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace Alloy.Api.Services
{
    public interface IAlloyBackgroundService : IHostedService
    {
    }

    public class AlloyBackgroundService : IAlloyBackgroundService
    {
        private readonly ILogger<AlloyBackgroundService> _logger;
        private readonly IOptionsMonitor<Infrastructure.Options.ClientOptions> _clientOptions;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IAlloyEventQueue _eventQueue;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly StartupHealthCheck _startupHealthCheck;
        private readonly TelemetryService _telemetryService;

        public AlloyBackgroundService(
                ILogger<AlloyBackgroundService> logger,
                IOptionsMonitor<Infrastructure.Options.ClientOptions> clientOptions,
                IServiceScopeFactory scopeFactory,
                IAlloyEventQueue eventQueue,
                IHttpClientFactory httpClientFactory,
                StartupHealthCheck startupHealthCheck,
                TelemetryService telemetryService
            )
        {
            _logger = logger;
            _clientOptions = clientOptions;
            _scopeFactory = scopeFactory;
            _eventQueue = eventQueue;
            _httpClientFactory = httpClientFactory;
            _startupHealthCheck = startupHealthCheck;
            _telemetryService = telemetryService;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = Run();

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Warn when non-positive retry limits allow failures to be retried indefinitely.
        /// </summary>
        private void WarnOnUnboundedRetries()
        {
            var options = _clientOptions.CurrentValue;

            if (options.ApiClientLaunchFailureMaxRetries <= 0)
            {
                _logger.LogWarning("ClientSettings:ApiClientLaunchFailureMaxRetries is {Value}. Failing launches will be retried forever and will never reach a Failed status. Set it to a positive number (10 is the shipped default) to bound them.", options.ApiClientLaunchFailureMaxRetries);
            }

            if (options.ApiClientEndFailureMaxRetries <= 0)
            {
                _logger.LogWarning("ClientSettings:ApiClientEndFailureMaxRetries is {Value}. Failing teardowns will be retried forever, and Bootstrap will not reclaim Failed Events that still hold external resources. Set it to a positive number (10 is the shipped default).", options.ApiClientEndFailureMaxRetries);
            }
        }

        /// <summary>
        /// Bootstraps (loads data) Events that were in process when this api encounters a stop/start cycle
        /// </summary>
        private async Task Bootstrap()
        {
            _logger.LogInformation($"AlloyBackgroundService is starting Bootstrap.");
            var bootstrapComplete = false;
            while (!bootstrapComplete)
            {
                try
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        using (var alloyContext = scope.ServiceProvider.GetRequiredService<AlloyContext>())
                        {
                            var maxEndRetries = _clientOptions.CurrentValue.ApiClientEndFailureMaxRetries;

                            // get event entities that are currently "in process"
                            var eventEntities = await alloyContext.Events
                                .Where(o => (o.Status != EventStatus.Active &&
                                        o.Status != EventStatus.Failed &&
                                        o.Status != EventStatus.Ended &&
                                        o.Status != EventStatus.Expired) ||
                                    ((o.Status == EventStatus.Active || o.Status == EventStatus.Paused) &&
                                        o.EndRequestedAt != null && o.EndDate == null) ||
                                    // Retry failed cleanup with remaining resources and retry budget.
                                    (o.Status == EventStatus.Failed &&
                                        (o.WorkspaceId != null || o.ViewId != null || o.ScenarioId != null) &&
                                        o.FailureCount < maxEndRetries))
                                .ToListAsync();

                            if (eventEntities.Any())
                            {
                                _logger.LogDebug($"AlloyBackgroundService is queueing {eventEntities.Count} Events.");
                                foreach (var eventEntity in eventEntities)
                                {
                                    if (eventEntity.Status == EventStatus.Failed)
                                    {
                                        _logger.LogInformation("AlloyBackgroundService is retrying cleanup of failed Event {EventId}, which still holds external resources.", eventEntity.Id);
                                        // ProcessTheEvent's loop does not run for Failed, so put the
                                        // Event back into a state the teardown states can pick up.
                                        eventEntity.Status = EventStatus.Ending;
                                        eventEntity.InternalStatus = InternalEventStatus.EndQueued;
                                        eventEntity.StatusDate = DateTime.UtcNow;
                                    }

                                    _logger.LogDebug($"AlloyBackgroundService is queueing Event {eventEntity.Id}.");
                                }

                                // ProcessTheEvent re-reads each Event in its own scope, so the reset
                                // above has to be committed before anything is queued.
                                await alloyContext.SaveChangesAsync();

                                foreach (var eventEntity in eventEntities)
                                {
                                    _eventQueue.Add(eventEntity);
                                }
                            }
                        }
                    }
                    bootstrapComplete = true;
                    _startupHealthCheck.StartupTaskCompleted = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception encountered in AlloyBackgroundService Bootstrap.");
                    await Task.Delay(new TimeSpan(0, 0, _clientOptions.CurrentValue.BackgroundTimerIntervalSeconds));
                }
            }
            _logger.LogInformation("AlloyBackgroundService Bootstrap complete.");
        }

        private async Task Run()
        {
            WarnOnUnboundedRetries();

            await Bootstrap();

            await Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        _logger.LogDebug("The AlloyBackgroundService is ready to process events.");
                        // _implementatioQueue is a BlockingCollection, so this loop will sleep if nothing is in the queue
                        var eventEntity = _eventQueue.Take(new CancellationToken());
                        // process the eventEntity on a new thread
                        try
                        {
                            var newThread = new Thread(ProcessTheEvent);
                            newThread.Start(eventEntity);
                        }
                        catch
                        {
                            _eventQueue.Complete(eventEntity);
                            throw;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception encountered in AlloyBackgroundService Run loop.");
                    }
                }
            });
        }

        private async void ProcessTheEvent(Object eventEntityAsObject) =>
            await ProcessEventAsync((EventEntity)eventEntityAsObject, CancellationToken.None);

        internal async Task ProcessEventAsync(EventEntity eventEntity, CancellationToken ct)
        {
            _logger.LogDebug($"Processing Event {eventEntity.Id} for status '{eventEntity.Status}'.");
            // A recovery scan may have read this row just before another worker failed.
            // Only an explicit request queued from Failed may restart failed cleanup.
            var retryFailed = eventEntity.Status == EventStatus.Failed;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                using var alloyContext = scope.ServiceProvider.GetRequiredService<AlloyContext>();

                var retryCount = 0;
                var resourceCount = int.MaxValue;
                var resourceRetryCount = 0;
                var resetRetries = true;
                DateTime? settleDeadline = null;

                // get the alloy context entities required
                eventEntity = await alloyContext.Events
                    .FirstAsync(x => x.Id == eventEntity.Id, ct);
                var eventTemplateEntity = alloyContext.EventTemplates.First(x => x.Id == eventEntity.EventTemplateId);

                TokenResponse tokenResponse = null;
                CasterApiClient casterApiClient = null;
                PlayerApiClient playerApiClient = null;
                SteamfitterApiClient steamfitterApiClient = null;

                var updateTheEntity = false;
                var retry = false;

                // Retain the last failure for diagnostics if the retry budget runs out.
                ApiCallResult lastTransientFailure = null;

                // Refresh authorization on retry in case the token caused the failure.
                void RetryAfter(ApiCallResult result)
                {
                    lastTransientFailure = result;
                    tokenResponse = null;
                    retry = true;
                }

                void HandleLaunchFailure(ApiCallResult result)
                {
                    if (result.IsPermanent)
                    {
                        updateTheEntity = FailLaunch(eventEntity, result, ref retryCount, ref resetRetries);
                    }
                    else
                    {
                        RetryAfter(result);
                    }
                }

                void RetryEndAfter(ApiCallResult result)
                {
                    updateTheEntity = RecordEndFailure(eventEntity, result);
                    RetryAfter(result);
                }

                // Active/Paused end requests and explicitly retried Failed cleanup must
                // enter the state machine before its loop condition is evaluated.
                await AdoptPendingEndAsync(alloyContext, eventEntity, ct, allowFailed: retryFailed);
                if (eventEntity.Status == EventStatus.Ending && eventEntity.WorkspaceId != null &&
                    eventEntity.InternalStatus != InternalEventStatus.EndQueued)
                {
                    // Reconcile Caster once on recovery, including teardown states saved
                    // by older versions that could still have an unfinished launch run.
                    eventEntity.InternalStatus = InternalEventStatus.EndQueued;
                    await alloyContext.SaveChangesAsync(ct);
                }
                Task<bool> EndRequested() => alloyContext.Events.AsNoTracking()
                    .AnyAsync(x => x.Id == eventEntity.Id && x.EndRequestedAt != null, ct);

                // LOOP until this thread's process is complete
                while (eventEntity.Status == EventStatus.Creating ||
                    eventEntity.Status == EventStatus.Planning ||
                    eventEntity.Status == EventStatus.Applying ||
                    eventEntity.Status == EventStatus.Ending)
                {
                    // A failure before reload must not trigger post-launch end adoption.
                    var processingLaunch = false;

                    try
                    {
                        // the updateTheEntity flag is used to indicate if the event entity state should be updated at the end of this loop
                        // Reset before reload so a failure cannot reuse the previous iteration's flags.
                        updateTheEntity = false;
                        retry = false;

                        // Reload persisted intent and discard unsaved changes from a failed adoption.
                        // Keep database failures within the worker's retry handling.
                        await alloyContext.Entry(eventEntity).ReloadAsync(ct);

                        if (await AdoptPendingEndAsync(alloyContext, eventEntity, ct))
                        {
                            // ending gets its own retry budget
                            retryCount = 0;
                        }

                        processingLaunch = eventEntity.Status == EventStatus.Creating ||
                            eventEntity.Status == EventStatus.Planning ||
                            eventEntity.Status == EventStatus.Applying;

                        // each time through the loop, one state (case) is handled based on Status and InternalStatus.  This allows for retries of a failed state.
                        switch (eventEntity.Status)
                        {
                            // the "Creating" status means we are creating the initial player view, steamfitter scenario and caster workspace
                            case EventStatus.Creating:
                                {
                                    switch (eventEntity.InternalStatus)
                                    {
                                        case InternalEventStatus.LaunchQueued:
                                        case InternalEventStatus.CreatingView:
                                            {
                                                if (eventTemplateEntity.ViewId == null)
                                                {
                                                    eventEntity.InternalStatus = InternalEventStatus.CreatingScenario;
                                                    updateTheEntity = true;
                                                }
                                                else
                                                {
                                                    (playerApiClient, tokenResponse) = await RefreshClient(playerApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                    eventEntity.InternalStatus = InternalEventStatus.CreatingView;
                                                    var users = await alloyContext.EventMemberships
                                                        .Where(m => m.EventId == eventEntity.Id)
                                                        .Select(m => m.User)
                                                        .ToListAsync(ct);
                                                    var result = await PlayerApiExtensions.CreatePlayerViewAsync(playerApiClient, eventEntity, eventTemplateEntity, users, _logger, ct);
                                                    if (result.IsSuccess)
                                                    {
                                                        eventEntity.ViewId = result.Value;
                                                        eventEntity.InternalStatus = InternalEventStatus.CreatingScenario;
                                                        updateTheEntity = true;
                                                    }
                                                    else
                                                    {
                                                        HandleLaunchFailure(result);
                                                    }
                                                }
                                                break;
                                            }
                                        case InternalEventStatus.CreatingScenario:
                                            {
                                                if (eventTemplateEntity.ScenarioTemplateId == null)
                                                {
                                                    eventEntity.InternalStatus = InternalEventStatus.CreatingWorkspace;
                                                    updateTheEntity = true;
                                                }
                                                else
                                                {
                                                    (steamfitterApiClient, tokenResponse) = await RefreshClient(steamfitterApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                    var result = await SteamfitterApiExtensions.CreateSteamfitterScenarioAsync(steamfitterApiClient, eventEntity, (Guid)eventTemplateEntity.ScenarioTemplateId, _logger, ct);
                                                    if (result.IsSuccess)
                                                    {
                                                        eventEntity.ScenarioId = result.Value.Id;
                                                        eventEntity.InternalStatus = InternalEventStatus.CreatingWorkspace;
                                                        updateTheEntity = true;
                                                    }
                                                    else
                                                    {
                                                        HandleLaunchFailure(result);
                                                    }
                                                }
                                                break;
                                            }
                                        case InternalEventStatus.CreatingWorkspace:
                                            {
                                                if (eventTemplateEntity.DirectoryId == null)
                                                {
                                                    // There is no Caster directory, so start the scenario
                                                    var launchDate = DateTime.UtcNow;
                                                    eventEntity.LaunchDate = launchDate;
                                                    eventEntity.ExpirationDate = launchDate.AddHours(eventTemplateEntity.DurationHours);
                                                    eventEntity.Status = EventStatus.Applying;
                                                    eventEntity.InternalStatus = InternalEventStatus.StartingScenario;
                                                    updateTheEntity = true;
                                                }
                                                else
                                                {
                                                    var varsFileContent = "";
                                                    if (eventEntity.ViewId != null)
                                                    {
                                                        (playerApiClient, tokenResponse) = await RefreshClient(playerApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                        var varsResult = await CasterApiExtensions.GetCasterVarsFileContentAsync(eventEntity, playerApiClient, _logger, ct);
                                                        if (!varsResult.IsSuccess)
                                                        {
                                                            HandleLaunchFailure(varsResult);
                                                            break;
                                                        }
                                                        varsFileContent = varsResult.Value;
                                                    }

                                                    // Without a Player view, the workspace gets an empty variables file.
                                                    (casterApiClient, tokenResponse) = await RefreshClient(casterApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                    var workspaceResult = await CasterApiExtensions.CreateCasterWorkspaceAsync(casterApiClient, eventEntity, (Guid)eventTemplateEntity.DirectoryId, varsFileContent, eventTemplateEntity.UseDynamicHost, _logger, ct);
                                                    if (workspaceResult.IsSuccess)
                                                    {
                                                        eventEntity.WorkspaceId = workspaceResult.Value;
                                                        eventEntity.InternalStatus = InternalEventStatus.PlanningLaunch;
                                                        eventEntity.Status = EventStatus.Planning;
                                                        updateTheEntity = true;
                                                    }
                                                    else
                                                    {
                                                        HandleLaunchFailure(workspaceResult);
                                                    }
                                                }
                                                break;
                                            }
                                        default:
                                            {
                                                updateTheEntity = FailInvalidState(eventEntity, ref retryCount, ref resetRetries);
                                                break;
                                            }
                                    }
                                    break;
                                }
                            // the "Planning" state means that caster is planning a run
                            case EventStatus.Planning:
                                {
                                    switch (eventEntity.InternalStatus)
                                    {
                                        case InternalEventStatus.PlanningLaunch:
                                        case InternalEventStatus.PlanningRedeploy:
                                            {
                                                (casterApiClient, tokenResponse) = await RefreshClient(casterApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                var result = await CasterApiExtensions.CreateRunAsync(eventEntity, casterApiClient, false, _logger, ct);
                                                if (result.IsSuccess)
                                                {
                                                    eventEntity.RunId = result.Value;
                                                    updateTheEntity = true;

                                                    switch (eventEntity.InternalStatus)
                                                    {
                                                        case InternalEventStatus.PlanningLaunch:
                                                            eventEntity.InternalStatus = InternalEventStatus.PlannedLaunch;
                                                            break;
                                                        case InternalEventStatus.PlanningRedeploy:
                                                            eventEntity.InternalStatus = InternalEventStatus.PlannedRedeploy;
                                                            break;
                                                    }
                                                }
                                                else
                                                {
                                                    HandleLaunchFailure(result);
                                                }
                                                break;
                                            }
                                        case InternalEventStatus.PlannedLaunch:
                                        case InternalEventStatus.PlannedRedeploy:
                                            {
                                                (casterApiClient, tokenResponse) = await RefreshClient(casterApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                var result = await CasterApiExtensions.WaitForRunToBePlannedAsync(eventEntity, casterApiClient, _clientOptions.CurrentValue.CasterCheckIntervalSeconds, _clientOptions.CurrentValue.CasterPlanningMaxWaitMinutes, false, _logger, ct, EndRequested);
                                                if (result.IsEndRequested)
                                                {
                                                    await AdoptPendingEndAsync(alloyContext, eventEntity, ct);
                                                    retryCount = 0;
                                                    resetRetries = true;
                                                    break;
                                                }
                                                if (result.IsSuccess)
                                                {
                                                    eventEntity.Status = EventStatus.Applying;
                                                    updateTheEntity = true;

                                                    switch (eventEntity.InternalStatus)
                                                    {
                                                        case InternalEventStatus.PlannedLaunch:
                                                            eventEntity.InternalStatus = InternalEventStatus.ApplyingLaunch;
                                                            break;
                                                        case InternalEventStatus.PlannedRedeploy:
                                                            eventEntity.InternalStatus = InternalEventStatus.ApplyingRedeploy;
                                                            break;
                                                    }
                                                }
                                                else
                                                {
                                                    HandleLaunchFailure(result);
                                                }
                                                break;
                                            }
                                        default:
                                            {
                                                updateTheEntity = FailInvalidState(eventEntity, ref retryCount, ref resetRetries);
                                                break;
                                            }
                                    }
                                    break;
                                }
                            // the "Applying" state means caster is applying a run (deploying VM's, etc.)
                            case EventStatus.Applying:
                                {
                                    switch (eventEntity.InternalStatus)
                                    {
                                        case InternalEventStatus.ApplyingLaunch:
                                        case InternalEventStatus.ApplyingRedeploy:
                                            {
                                                (casterApiClient, tokenResponse) = await RefreshClient(casterApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                var result = await CasterApiExtensions.ApplyRunAsync(eventEntity, casterApiClient, _logger, ct);
                                                if (result.IsSuccess)
                                                {
                                                    updateTheEntity = true;

                                                    switch (eventEntity.InternalStatus)
                                                    {
                                                        case InternalEventStatus.ApplyingLaunch:
                                                            eventEntity.InternalStatus = InternalEventStatus.AppliedLaunch;
                                                            break;
                                                        case InternalEventStatus.ApplyingRedeploy:
                                                            eventEntity.InternalStatus = InternalEventStatus.AppliedRedeploy;
                                                            break;
                                                    }
                                                }
                                                else
                                                {
                                                    HandleLaunchFailure(result);
                                                }
                                                break;
                                            }
                                        case InternalEventStatus.AppliedLaunch:
                                        case InternalEventStatus.AppliedRedeploy:
                                            {
                                                (casterApiClient, tokenResponse) = await RefreshClient(casterApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                var result = await CasterApiExtensions.WaitForRunToBeAppliedAsync(eventEntity, casterApiClient, _clientOptions.CurrentValue.CasterCheckIntervalSeconds, _clientOptions.CurrentValue.CasterDeployMaxWaitMinutes, false, _logger, ct, EndRequested);
                                                if (result.IsEndRequested)
                                                {
                                                    await AdoptPendingEndAsync(alloyContext, eventEntity, ct);
                                                    retryCount = 0;
                                                    resetRetries = true;
                                                    break;
                                                }
                                                if (result.IsSuccess)
                                                {
                                                    updateTheEntity = true;

                                                    switch (eventEntity.InternalStatus)
                                                    {
                                                        case InternalEventStatus.AppliedLaunch:
                                                            eventEntity.InternalStatus = InternalEventStatus.StartingScenario;
                                                            break;
                                                        case InternalEventStatus.AppliedRedeploy:
                                                            eventEntity.Status = EventStatus.Active;
                                                            eventEntity.InternalStatus = InternalEventStatus.Launched;
                                                            // a redeploy that worked clears whatever went wrong last time
                                                            eventEntity.ClearFailureState();
                                                            break;
                                                    }

                                                    resetRetries = true;
                                                }
                                                else
                                                {
                                                    HandleLaunchFailure(result);
                                                }
                                                break;
                                            }
                                        case InternalEventStatus.StartingScenario:
                                            {
                                                // start the steamfitter scenario, if there is one
                                                var result = ApiCallResult.Ok();
                                                if (eventEntity.ScenarioId != null)
                                                {
                                                    (steamfitterApiClient, tokenResponse) = await RefreshClient(steamfitterApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                    result = await SteamfitterApiExtensions.StartSteamfitterScenarioAsync(steamfitterApiClient, (Guid)eventEntity.ScenarioId, _logger, ct);
                                                }
                                                // moving on means that Launch is now complete
                                                if (result.IsSuccess)
                                                {
                                                    var launchDate = DateTime.UtcNow;
                                                    eventEntity.LaunchDate = launchDate;
                                                    eventEntity.ExpirationDate = launchDate.AddHours(eventTemplateEntity.DurationHours);
                                                    eventEntity.Status = EventStatus.Active;
                                                    eventEntity.InternalStatus = InternalEventStatus.Launched;
                                                    eventEntity.ClearFailureState();
                                                    updateTheEntity = true;
                                                }
                                                else
                                                {
                                                    HandleLaunchFailure(result);
                                                }
                                                break;
                                            }
                                        default:
                                            {
                                                updateTheEntity = FailInvalidState(eventEntity, ref retryCount, ref resetRetries);
                                                break;
                                            }
                                    }
                                    break;
                                }
                            // the "Ending" state means all entities are being torn down
                            case EventStatus.Ending:
                                {
                                    switch (eventEntity.InternalStatus)
                                    {
                                        case InternalEventStatus.EndQueued:
                                            {
                                                settleDeadline ??= DateTime.UtcNow.AddMinutes(_clientOptions.CurrentValue.CasterDestroyMaxWaitMinutes);
                                                if (eventEntity.WorkspaceId != null)
                                                    (casterApiClient, tokenResponse) = await RefreshClient(casterApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                var result = await CasterApiExtensions.SettleLaunchRunsAsync(eventEntity, casterApiClient,
                                                    _clientOptions.CurrentValue.CasterCheckIntervalSeconds, settleDeadline.Value, _logger, ct);
                                                if (result.IsSuccess)
                                                {
                                                    var destroyRun = result.Value;
                                                    eventEntity.RunId = destroyRun?.Id;
                                                    eventEntity.InternalStatus = destroyRun == null
                                                        ? InternalEventStatus.PlanningDestroy
                                                        : destroyRun.Status == RunStatus.Planned
                                                            ? InternalEventStatus.ApplyingDestroy
                                                            : destroyRun.Status == RunStatus.Queued || destroyRun.Status == RunStatus.Planning
                                                                ? InternalEventStatus.PlannedDestroy
                                                                : InternalEventStatus.AppliedDestroy;
                                                    updateTheEntity = true;
                                                    resetRetries = true;
                                                }
                                                else if (DateTime.UtcNow >= settleDeadline.Value)
                                                {
                                                    updateTheEntity = FailEnd(eventEntity, result, ref retryCount);
                                                }
                                                else
                                                {
                                                    RetryEndAfter(result);
                                                }
                                                break;
                                            }
                                        case InternalEventStatus.PlanningDestroy:
                                            {
                                                if (eventEntity.WorkspaceId != null)
                                                {
                                                    (casterApiClient, tokenResponse) = await RefreshClient(casterApiClient, tokenResponse, scope.ServiceProvider, ct);

                                                    var countResult = await CasterApiExtensions.GetWorkspaceResourceCountAsync(eventEntity, casterApiClient, _logger, ct);

                                                    if (!countResult.IsSuccess)
                                                    {
                                                        // Don't start a destroy run on a guess about what is in there.
                                                        RetryEndAfter(countResult);
                                                    }
                                                    // if no resources, skip to deleting workspace
                                                    else if (countResult.Value == 0)
                                                    {
                                                        eventEntity.InternalStatus = InternalEventStatus.DeletingWorkspace;
                                                        updateTheEntity = true;
                                                    }
                                                    else
                                                    {
                                                        var result = await CasterApiExtensions.CreateRunAsync(eventEntity, casterApiClient, true, _logger, ct);
                                                        if (result.IsSuccess)
                                                        {
                                                            eventEntity.RunId = result.Value;
                                                            eventEntity.InternalStatus = InternalEventStatus.PlannedDestroy;
                                                            updateTheEntity = true;
                                                        }
                                                        else
                                                        {
                                                            // Never give up on teardown here, even for a permanent failure:
                                                            // only the retry ceiling in the finally block may stop trying,
                                                            // and it records why when it does.
                                                            RetryEndAfter(result);
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    eventEntity.InternalStatus = InternalEventStatus.DeletingView;
                                                    updateTheEntity = true;
                                                }
                                                break;
                                            }
                                        case InternalEventStatus.PlannedDestroy:
                                            {
                                                (casterApiClient, tokenResponse) = await RefreshClient(casterApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                var result = await CasterApiExtensions.WaitForRunToBePlannedAsync(eventEntity, casterApiClient, _clientOptions.CurrentValue.CasterCheckIntervalSeconds, _clientOptions.CurrentValue.CasterPlanningMaxWaitMinutes, true, _logger, ct);
                                                if (result.IsSuccess)
                                                {
                                                    eventEntity.InternalStatus = InternalEventStatus.ApplyingDestroy;
                                                    updateTheEntity = true;
                                                }
                                                else
                                                {
                                                    updateTheEntity = RecordEndFailure(eventEntity, result);
                                                    if (result.IsPermanent)
                                                    {
                                                        // Only replace a rejected/failed plan, not a
                                                        // plan whose status could not be read.
                                                        eventEntity.InternalStatus = InternalEventStatus.PlanningDestroy;
                                                        updateTheEntity = true;
                                                        resetRetries = false;
                                                    }
                                                    RetryAfter(result);
                                                }
                                                break;
                                            }
                                        case InternalEventStatus.ApplyingDestroy:
                                            {
                                                (casterApiClient, tokenResponse) = await RefreshClient(casterApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                var result = await CasterApiExtensions.ApplyRunAsync(eventEntity, casterApiClient, _logger, ct);
                                                if (result.IsSuccess)
                                                {
                                                    eventEntity.InternalStatus = InternalEventStatus.AppliedDestroy;
                                                    updateTheEntity = true;
                                                }
                                                else
                                                {
                                                    RetryEndAfter(result);
                                                }
                                                break;
                                            }
                                        case InternalEventStatus.AppliedDestroy:
                                            {
                                                (casterApiClient, tokenResponse) = await RefreshClient(casterApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                var applyResult = await CasterApiExtensions.WaitForRunToBeAppliedAsync(eventEntity, casterApiClient, _clientOptions.CurrentValue.CasterCheckIntervalSeconds, _clientOptions.CurrentValue.CasterDestroyMaxWaitMinutes, true, _logger, ct);
                                                if (!applyResult.IsSuccess)
                                                {
                                                    updateTheEntity = RecordEndFailure(eventEntity, applyResult);
                                                    if (applyResult.IsTransient)
                                                    {
                                                        // Resources may still be changing. Keep the
                                                        // run ID and observe this apply again.
                                                        RetryAfter(applyResult);
                                                        break;
                                                    }
                                                }

                                                // make sure that the run successfully deleted the resources
                                                var countResult = await CasterApiExtensions.GetWorkspaceResourceCountAsync(eventEntity, casterApiClient, _logger, ct);
                                                if (!countResult.IsSuccess)
                                                {
                                                    // Without a count there is nothing to judge progress by, so ask
                                                    // again rather than spend one of the destroy attempts.
                                                    RetryEndAfter(countResult);
                                                    break;
                                                }

                                                // all remaining conditions in this case require an event entity update
                                                updateTheEntity = true;
                                                var count = countResult.Value;
                                                eventEntity.RunId = null;
                                                if (count == 0)
                                                {
                                                    // resources deleted, so continue to delete the workspace
                                                    eventEntity.InternalStatus = InternalEventStatus.DeletingWorkspace;
                                                    resetRetries = true;
                                                }
                                                else
                                                {
                                                    if (count < resourceCount)
                                                    {
                                                        // still some resources, but making progress, try the whole process again
                                                        eventEntity.InternalStatus = InternalEventStatus.PlanningDestroy;
                                                        resourceRetryCount = 0;
                                                    }
                                                    else
                                                    {
                                                        // still some resources and not making progress. Check max retries.
                                                        // Math.Max(1, ...) because the retry ceilings elsewhere read 0 as
                                                        // "retry forever"; without it, 0 here means "give up immediately"
                                                        // and the two readings disagree.
                                                        if (resourceRetryCount < Math.Max(1, _clientOptions.CurrentValue.ApiClientEndFailureMaxRetries))
                                                        {
                                                            // try the whole process again after a wait
                                                            eventEntity.InternalStatus = InternalEventStatus.PlanningDestroy;
                                                            resourceRetryCount++;
                                                            await Task.Delay(TimeSpan.FromMinutes(_clientOptions.CurrentValue.CasterDestroyRetryDelayMinutes), ct);
                                                        }
                                                        else
                                                        {
                                                            // the caster workspace resources could not be destroyed
                                                            updateTheEntity = FailEnd(eventEntity, ApiCallResult.Permanent(
                                                                $"{count} infrastructure resources could not be destroyed. Please contact an administrator.",
                                                                $"Caster Workspace {eventEntity.WorkspaceId} still has {count} resources after {resourceRetryCount} destroy attempts."),
                                                                ref retryCount);
                                                        }
                                                    }

                                                    resourceCount = count;
                                                }
                                                break;
                                            }
                                        case InternalEventStatus.DeletingWorkspace:
                                            {
                                                (casterApiClient, tokenResponse) = await RefreshClient(casterApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                var result = await CasterApiExtensions.DeleteCasterWorkspaceAsync(eventEntity, casterApiClient, _logger, ct);
                                                if (result.IsSuccess)
                                                {
                                                    eventEntity.WorkspaceId = null;
                                                    eventEntity.InternalStatus = InternalEventStatus.DeletingView;
                                                    updateTheEntity = true;
                                                }
                                                else
                                                {
                                                    RetryEndAfter(result);
                                                }
                                                break;
                                            }
                                        case InternalEventStatus.DeletingView:
                                            {
                                                var result = ApiCallResult.Ok();
                                                if (eventEntity.ViewId != null)
                                                {
                                                    (playerApiClient, tokenResponse) = await RefreshClient(playerApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                    result = await PlayerApiExtensions.DeletePlayerViewAsync(eventEntity.ViewId, playerApiClient, _logger, ct);
                                                }
                                                if (result.IsSuccess)
                                                {
                                                    eventEntity.ViewId = null;
                                                    eventEntity.InternalStatus = InternalEventStatus.DeletingScenario;
                                                    updateTheEntity = true;
                                                }
                                                else
                                                {
                                                    RetryEndAfter(result);
                                                }
                                                break;
                                            }
                                        case InternalEventStatus.DeletingScenario:
                                            {
                                                var result = ApiCallResult.Ok();
                                                if (eventEntity.ScenarioId != null)
                                                {
                                                    (steamfitterApiClient, tokenResponse) = await RefreshClient(steamfitterApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                    result = await SteamfitterApiExtensions.EndSteamfitterScenarioAsync(eventEntity.ScenarioId, steamfitterApiClient, _logger, ct);
                                                }
                                                if (result.IsSuccess)
                                                {
                                                    eventEntity.ScenarioId = null;
                                                    eventEntity.EndDate = DateTime.UtcNow;

                                                    // Successful cleanup must preserve the outcome of a failed launch.
                                                    if (eventEntity.LastLaunchInternalStatus != default)
                                                    {
                                                        eventEntity.Status = EventStatus.Failed;
                                                        eventEntity.InternalStatus = InternalEventStatus.FailedLaunch;
                                                    }
                                                    else
                                                    {
                                                        eventEntity.Status = EventStatus.Ended;
                                                        eventEntity.InternalStatus = InternalEventStatus.Ended;

                                                        // Clear errors from cleanup attempts that subsequently succeeded.
                                                        eventEntity.ClearFailureState();
                                                    }
                                                    updateTheEntity = true;
                                                }
                                                else
                                                {
                                                    RetryEndAfter(result);
                                                }
                                                break;
                                            }

                                        default:
                                            {
                                                updateTheEntity = FailInvalidState(eventEntity, ref retryCount, ref resetRetries);
                                                break;
                                            }
                                    }
                                    break;
                                }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing Event {EventId} at {Status} - {InternalStatus}", eventEntity.Id, eventEntity.Status, eventEntity.InternalStatus);

                        var result = ex.Classify("process the event");
                        if (eventEntity.Status == EventStatus.Ending)
                        {
                            RetryEndAfter(result);
                        }
                        else
                        {
                            HandleLaunchFailure(result);
                        }
                    }
                    finally
                    {
                        // check for exceeding the max number of retries
                        if (retry)
                        {
                            retryCount++;
                            var launchMaxRetries = _clientOptions.CurrentValue.ApiClientLaunchFailureMaxRetries;
                            var endMaxRetries = _clientOptions.CurrentValue.ApiClientEndFailureMaxRetries;

                            if (eventEntity.Status == EventStatus.Ending &&
                                eventEntity.InternalStatus == InternalEventStatus.EndQueued &&
                                settleDeadline.HasValue && DateTime.UtcNow >= settleDeadline.Value)
                            {
                                // Authentication can fail before the Caster helper runs.
                                // Its deadline still applies even when API retries are unbounded.
                                updateTheEntity = FailEnd(eventEntity, ApiCallResult.Permanent(
                                    "The infrastructure run could not be confirmed stopped in time. Cleanup can be retried by an administrator.",
                                    lastTransientFailure?.Detail), ref retryCount);
                            }
                            else if ((eventEntity.Status == EventStatus.Creating ||
                                    eventEntity.Status == EventStatus.Planning ||
                                    eventEntity.Status == EventStatus.Applying) &&
                                retryCount >= launchMaxRetries && launchMaxRetries > 0)
                            {
                                // Same state as a permanent launch failure, by construction: the
                                // Event goes to teardown and the reason is recorded once.
                                updateTheEntity = FailLaunch(eventEntity, CeilingFailure(
                                    "The event could not be launched. Please try again, or contact an administrator if it keeps failing.",
                                    launchMaxRetries, eventEntity, lastTransientFailure),
                                    ref retryCount, ref resetRetries);
                            }
                            else if (eventEntity.Status == EventStatus.Ending &&
                                retryCount >= endMaxRetries && endMaxRetries > 0)
                            {
                                updateTheEntity = FailEnd(eventEntity, CeilingFailure(
                                    "The event could not be cleaned up. Please contact an administrator.",
                                    endMaxRetries, eventEntity, lastTransientFailure),
                                    ref retryCount);
                            }
                            else
                            {
                                await Task.Delay(TimeSpan.FromSeconds(_clientOptions.CurrentValue.ApiClientRetryIntervalSeconds), ct);
                            }

                        }
                        else
                        {
                            // The state moved on, so whatever failed on the way here has been
                            // recovered from and is no longer the reason for anything.
                            lastTransientFailure = null;

                            if (resetRetries)
                            {
                                retryCount = 0;
                            }
                        }

                        // update the entity in the context, if we are moving on
                        if (updateTheEntity)
                        {
                            eventEntity.StatusDate = DateTime.UtcNow;
                            await alloyContext.SaveChangesAsync(ct);

                            // Persist any resource acquired by the step before adopting end intent.
                            if (processingLaunch)
                            {
                                // The next pass or periodic recovery retries a failed adoption,
                                // including a request persisted just as the event became Active.
                                try
                                {
                                    if (await AdoptPendingEndAsync(alloyContext, eventEntity, ct))
                                    {
                                        // ending gets its own retry budget
                                        retryCount = 0;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Could not pick up the pending end of Event {EventId} at {Status} - {InternalStatus}. The next pass will retry.",
                                        eventEntity.Id, eventEntity.Status, eventEntity.InternalStatus);
                                }
                            }
                        }
                    }
                }

                // Update Event metrics
                await _telemetryService.UpdateEventGauges(alloyContext, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing event {eventEntity.Id}. Terminating");
            }
            finally
            {
                _eventQueue.Complete(eventEntity);
            }
        }

        /// <summary>Only the worker changes operational state in response to end intent.</summary>
        private async Task<bool> AdoptPendingEndAsync(AlloyContext alloyContext, EventEntity eventEntity,
            CancellationToken ct, bool allowFailed = false)
        {
            if (eventEntity.EndDate != null ||
                (eventEntity.Status != EventStatus.Creating && eventEntity.Status != EventStatus.Planning &&
                 eventEntity.Status != EventStatus.Applying && eventEntity.Status != EventStatus.Active &&
                 eventEntity.Status != EventStatus.Paused && !(allowFailed && eventEntity.Status == EventStatus.Failed)))
                return false;

            var requestedAt = await alloyContext.Events.AsNoTracking()
                .Where(x => x.Id == eventEntity.Id).Select(x => x.EndRequestedAt).FirstOrDefaultAsync(ct);
            if (requestedAt == null)
                return false;

            _logger.LogInformation("Ending Event {EventId} after request at {EndRequestedAt}.", eventEntity.Id, requestedAt);
            eventEntity.EndRequestedAt = requestedAt;
            eventEntity.Status = EventStatus.Ending;
            eventEntity.InternalStatus = InternalEventStatus.EndQueued;
            eventEntity.StatusDate = DateTime.UtcNow;
            await alloyContext.SaveChangesAsync(ct);
            return true;
        }

        /// <summary>
        /// Marker separating the reason a launch failed from the reason its cleanup then failed.
        /// </summary>
        private const string CleanupFailureSeparator = "\n\nCleanup failed: ";

        /// <summary>
        /// Reports an exhausted retry budget with the last failure's diagnostics.
        /// The summary describes the terminal outcome rather than promising another retry.
        /// </summary>
        private static ApiCallResult CeilingFailure(
            string summary, int maxRetries, EventEntity eventEntity, ApiCallResult lastFailure)
        {
            var detail = new StringBuilder()
                .Append($"Gave up after {maxRetries} attempts at {eventEntity.Status} - {eventEntity.InternalStatus}.");

            if (lastFailure != null)
            {
                detail.AppendLine().AppendLine().Append($"Last failure: {lastFailure.Summary}");

                if (!string.IsNullOrWhiteSpace(lastFailure.Detail))
                {
                    detail.AppendLine().AppendLine().Append(lastFailure.Detail);
                }
            }

            return ApiCallResult.Permanent(summary, detail.ToString());
        }

        /// <summary>
        /// Records a permanent launch failure and hands the Event to the teardown states.
        /// </summary>
        private bool FailLaunch(EventEntity eventEntity, ApiCallResult result, ref int retryCount, ref bool resetRetries)
        {
            _logger.LogError("Launch of Event {EventId} failed at {Status} - {InternalStatus}: {Summary}",
                eventEntity.Id, eventEntity.Status, eventEntity.InternalStatus, result.Summary);

            RecordError(eventEntity, result);

            // Captured before Status/InternalStatus are overwritten below. These two are also the
            // signal DeletingScenario uses to decide whether teardown ends in Ended or Failed.
            eventEntity.LastLaunchStatus = eventEntity.Status;
            eventEntity.LastLaunchInternalStatus = eventEntity.InternalStatus;
            eventEntity.FailureCount++;

            // Keep the worker running until acquired resources are cleaned up.
            eventEntity.Status = EventStatus.Ending;
            eventEntity.InternalStatus = InternalEventStatus.EndQueued;

            // give teardown a full retry budget of its own
            retryCount = 0;
            resetRetries = true;

            return true;
        }

        /// <summary>
        /// Records a cleanup failure. Returns true only when diagnostics change,
        /// avoiding repeated saves and notifications for identical failures.
        /// </summary>
        private bool RecordEndFailure(EventEntity eventEntity, ApiCallResult result)
        {
            _logger.LogError("Cleanup of Event {EventId} failed at {Status} - {InternalStatus}: {Summary}",
                eventEntity.Id, eventEntity.Status, eventEntity.InternalStatus, result.Summary);

            // The launch-failure marker distinguishes a launch error from an earlier cleanup error.
            var launchReason = string.Empty;

            if (eventEntity.LastLaunchInternalStatus != default)
            {
                // Replace the previous cleanup note while preserving the launch reason.
                launchReason = eventEntity.ErrorMessage ?? string.Empty;
                var separatorIndex = launchReason.IndexOf(CleanupFailureSeparator, StringComparison.Ordinal);
                if (separatorIndex >= 0)
                {
                    launchReason = launchReason.Substring(0, separatorIndex);
                }
            }

            var message = (string.IsNullOrEmpty(launchReason)
                    ? result.Summary
                    : launchReason + CleanupFailureSeparator + result.Summary)
                .Truncate(EventErrorLimits.MaxSummaryLength);

            // Keep the latest diagnostics even when the summary is unchanged.
            // An empty detail leaves the existing diagnostics intact.
            var detail = string.IsNullOrEmpty(result.Detail)
                ? eventEntity.ErrorDetail
                : result.Detail.StripAnsi().TruncateTail(EventErrorLimits.MaxDetailLength);

            if (message == eventEntity.ErrorMessage && detail == eventEntity.ErrorDetail)
            {
                return false;
            }

            eventEntity.ErrorMessage = message;
            eventEntity.ErrorDetail = detail;

            return true;
        }

        /// <summary>
        /// Gives up on teardown. This is the only path that may set <see cref="EventStatus.Failed"/>
        /// while resources may still exist; Bootstrap will pick the Event back up on the next
        /// restart while FailureCount is still under the ceiling.
        /// </summary>
        private bool FailEnd(EventEntity eventEntity, ApiCallResult result, ref int retryCount)
        {
            _logger.LogError("Cleanup of Event {EventId} gave up at {Status} - {InternalStatus}: {Summary}",
                eventEntity.Id, eventEntity.Status, eventEntity.InternalStatus, result.Summary);

            RecordEndFailure(eventEntity, result);

            eventEntity.LastEndStatus = eventEntity.Status;
            eventEntity.LastEndInternalStatus = eventEntity.InternalStatus;
            eventEntity.FailureCount++;
            eventEntity.Status = EventStatus.Failed;
            eventEntity.InternalStatus = InternalEventStatus.FailedDestroy;
            retryCount = 0;

            return true;
        }

        /// <summary>
        /// Routes an invalid launch state to cleanup, or stops cleanup in an invalid end state.
        /// </summary>
        private bool FailInvalidState(EventEntity eventEntity, ref int retryCount, ref bool resetRetries)
        {
            _logger.LogError("Invalid status for Event {EventId}: {Status} - {InternalStatus}",
                eventEntity.Id, eventEntity.Status, eventEntity.InternalStatus);

            var result = ApiCallResult.Permanent(
                "This event is in an unexpected state and cannot continue. Please contact an administrator.",
                $"Unhandled state combination {eventEntity.Status} - {eventEntity.InternalStatus}.");

            return eventEntity.Status == EventStatus.Ending
                ? FailEnd(eventEntity, result, ref retryCount)
                : FailLaunch(eventEntity, result, ref retryCount, ref resetRetries);
        }

        private static void RecordError(EventEntity eventEntity, ApiCallResult result)
        {
            eventEntity.ErrorMessage = result.Summary.Truncate(EventErrorLimits.MaxSummaryLength);
            eventEntity.ErrorDetail = result.Detail?.StripAnsi().TruncateTail(EventErrorLimits.MaxDetailLength);
        }

        private async Task<(PlayerApiClient, TokenResponse)> RefreshClient(PlayerApiClient clientObject, TokenResponse tokenResponse, IServiceProvider serviceProvider, CancellationToken ct)
        {
            // Check if token is null or expired
            bool tokenExpired = tokenResponse != null && tokenResponse.ExpiresIn <= 60; // Refresh if less than 60 seconds left

            if (clientObject == null || tokenResponse == null || tokenExpired)
            {
                tokenResponse = await ApiClientsExtensions.GetToken(serviceProvider);
                clientObject = PlayerApiExtensions.GetPlayerApiClient(_httpClientFactory, _clientOptions.CurrentValue.urls.playerApi, tokenResponse);
            }

            return (clientObject, tokenResponse);
        }

        private async Task<(SteamfitterApiClient, TokenResponse)> RefreshClient(SteamfitterApiClient clientObject, TokenResponse tokenResponse, IServiceProvider serviceProvider, CancellationToken ct)
        {
            // Check if token is null or expired
            bool tokenExpired = tokenResponse != null && tokenResponse.ExpiresIn <= 60; // Refresh if less than 60 seconds left

            if (clientObject == null || tokenResponse == null || tokenExpired)
            {
                tokenResponse = await ApiClientsExtensions.GetToken(serviceProvider);
                clientObject = SteamfitterApiExtensions.GetSteamfitterApiClient(_httpClientFactory, _clientOptions.CurrentValue.urls.steamfitterApi, tokenResponse);
            }

            return (clientObject, tokenResponse);
        }

        private async Task<(CasterApiClient, TokenResponse)> RefreshClient(CasterApiClient clientObject, TokenResponse tokenResponse, IServiceProvider serviceProvider, CancellationToken ct)
        {
            // Check if token is null or expired
            bool tokenExpired = tokenResponse != null && tokenResponse.ExpiresIn <= 60; // Refresh if less than 60 seconds left

            if (clientObject == null || tokenResponse == null || tokenExpired)
            {
                tokenResponse = await ApiClientsExtensions.GetToken(serviceProvider);
                clientObject = CasterApiExtensions.GetCasterApiClient(_httpClientFactory, _clientOptions.CurrentValue.urls.casterApi, tokenResponse);
            }

            return (clientObject, tokenResponse);
        }

    }
}
