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
        /// The retry ceilings in the finally block only apply when they are greater than zero, so a
        /// value of zero means "retry forever" - an Event that cannot be launched then sits in
        /// Planning indefinitely and never reports why. Say so at startup rather than letting an
        /// operator discover it from a stuck Event.
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
                                    // A Failed Event that still holds external resources has orphans
                                    // out there: nothing else reclaims it, because AlloyQueryService
                                    // only expires Events with an ExpirationDate, and a launch that
                                    // failed never got one. Give teardown another chance rather than
                                    // leaking a Player View, a Steamfitter Scenario and a Caster
                                    // Workspace forever. Self-limiting, since FailureCount only grows.
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
                        var newThread = new Thread(ProcessTheEvent);
                        newThread.Start(eventEntity);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception encountered in AlloyBackgroundService Run loop.");
                    }
                }
            });
        }

        private async void ProcessTheEvent(Object eventEntityAsObject)
        {
            var ct = new CancellationToken();
            var eventEntity = eventEntityAsObject == null ? (EventEntity)null : (EventEntity)eventEntityAsObject;
            _logger.LogDebug($"Processing Event {eventEntity.Id} for status '{eventEntity.Status}'.");

            try
            {
                using var scope = _scopeFactory.CreateScope();
                using var alloyContext = scope.ServiceProvider.GetRequiredService<AlloyContext>();

                var retryCount = 0;
                var resourceCount = int.MaxValue;
                var resourceRetryCount = 0;
                var resetRetries = true;

                // get the alloy context entities required
                eventEntity = await alloyContext.Events
                    .FirstAsync(x => x.Id == eventEntity.Id);
                var eventTemplateEntity = alloyContext.EventTemplates.First(x => x.Id == eventEntity.EventTemplateId);

                TokenResponse tokenResponse = null;
                CasterApiClient casterApiClient = null;
                PlayerApiClient playerApiClient = null;
                SteamfitterApiClient steamfitterApiClient = null;

                var updateTheEntity = false;
                var retry = false;

                // The most recent transient failure. Transient arms deliberately record nothing on
                // the Event - a failure the next pass recovers from is not something anyone should
                // have to read about - but if the retries run out then this is the only account of
                // what actually went wrong, so the ceiling in the finally block reports it.
                ApiCallResult lastTransientFailure = null;

                // What every transient arm does: remember the reason, discard the resource-owner
                // token in case it was the problem, and go round again.
                void RetryAfter(ApiCallResult result)
                {
                    lastTransientFailure = result;
                    tokenResponse = null;
                    retry = true;
                }

                // LOOP until this thread's process is complete
                while (eventEntity.Status == EventStatus.Creating ||
                    eventEntity.Status == EventStatus.Planning ||
                    eventEntity.Status == EventStatus.Applying ||
                    eventEntity.Status == EventStatus.Ending)
                {
                    // Another request can transition this Event while a long-running
                    // launch operation is in flight. Always begin the next state
                    // transition from the persisted state.
                    await alloyContext.Entry(eventEntity).ReloadAsync(ct);

                    if (await AdoptPendingEndAsync(alloyContext, eventEntity, ct))
                    {
                        // ending gets its own retry budget
                        retryCount = 0;
                    }

                    var processingLaunch = eventEntity.Status == EventStatus.Creating ||
                        eventEntity.Status == EventStatus.Planning ||
                        eventEntity.Status == EventStatus.Applying;

                    try
                    {
                        // the updateTheEntity flag is used to indicate if the event entity state should be updated at the end of this loop
                        updateTheEntity = false;
                        retry = false;

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
                                                // The Event's Name and Description are set when the
                                                // Event is created (EventService.CreateEventEntityAsync).

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
                                                    else if (result.IsPermanent)
                                                    {
                                                        updateTheEntity = FailLaunch(eventEntity, result, ref retryCount, ref resetRetries);
                                                    }
                                                    else
                                                    {
                                                        RetryAfter(result);
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
                                                    else if (result.IsPermanent)
                                                    {
                                                        updateTheEntity = FailLaunch(eventEntity, result, ref retryCount, ref resetRetries);
                                                    }
                                                    else
                                                    {
                                                        RetryAfter(result);
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
                                                    var varsResult = ApiCallResult<string>.Ok("");
                                                    if (eventEntity.ViewId != null)
                                                    {
                                                        (playerApiClient, tokenResponse) = await RefreshClient(playerApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                        varsResult = await CasterApiExtensions.GetCasterVarsFileContentAsync(eventEntity, playerApiClient, _logger, ct);
                                                        varsFileContent = varsResult.Value;
                                                    }

                                                    if (varsResult.IsPermanent)
                                                    {
                                                        updateTheEntity = FailLaunch(eventEntity, varsResult, ref retryCount, ref resetRetries);
                                                    }
                                                    else if (!varsResult.IsSuccess)
                                                    {
                                                        RetryAfter(varsResult);
                                                    }
                                                    else
                                                    {
                                                        // An EventTemplate with a Caster directory but no Player view gets an
                                                        // empty tfvars file - which Terraform accepts - rather than retrying
                                                        // to the ceiling with nothing to report.
                                                        (casterApiClient, tokenResponse) = await RefreshClient(casterApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                        var workspaceResult = await CasterApiExtensions.CreateCasterWorkspaceAsync(casterApiClient, eventEntity, (Guid)eventTemplateEntity.DirectoryId, varsFileContent, eventTemplateEntity.UseDynamicHost, _logger, ct);
                                                        if (workspaceResult.IsSuccess)
                                                        {
                                                            eventEntity.WorkspaceId = workspaceResult.Value;
                                                            eventEntity.InternalStatus = InternalEventStatus.PlanningLaunch;
                                                            eventEntity.Status = EventStatus.Planning;
                                                            updateTheEntity = true;
                                                        }
                                                        else if (workspaceResult.IsPermanent)
                                                        {
                                                            updateTheEntity = FailLaunch(eventEntity, workspaceResult, ref retryCount, ref resetRetries);
                                                        }
                                                        else
                                                        {
                                                            RetryAfter(workspaceResult);
                                                        }
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
                                                else if (result.IsPermanent)
                                                {
                                                    updateTheEntity = FailLaunch(eventEntity, result, ref retryCount, ref resetRetries);
                                                }
                                                else
                                                {
                                                    RetryAfter(result);
                                                }
                                                break;
                                            }
                                        case InternalEventStatus.PlannedLaunch:
                                        case InternalEventStatus.PlannedRedeploy:
                                            {
                                                (casterApiClient, tokenResponse) = await RefreshClient(casterApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                var result = await CasterApiExtensions.WaitForRunToBePlannedAsync(eventEntity, casterApiClient, _clientOptions.CurrentValue.CasterCheckIntervalSeconds, _clientOptions.CurrentValue.CasterPlanningMaxWaitMinutes, _logger, ct);
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
                                                else if (result.IsPermanent)
                                                {
                                                    // Terraform rejected the plan; retrying cannot change that
                                                    updateTheEntity = FailLaunch(eventEntity, result, ref retryCount, ref resetRetries);
                                                }
                                                else
                                                {
                                                    // Plan timed out or hit a transient error, so plan again
                                                    lastTransientFailure = result;

                                                    switch (eventEntity.InternalStatus)
                                                    {
                                                        case InternalEventStatus.PlannedLaunch:
                                                            eventEntity.InternalStatus = InternalEventStatus.PlanningLaunch;
                                                            break;
                                                        case InternalEventStatus.PlannedRedeploy:
                                                            eventEntity.InternalStatus = InternalEventStatus.PlanningRedeploy;
                                                            break;
                                                    }

                                                    updateTheEntity = true;
                                                    retry = true;
                                                    resetRetries = false;
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
                                                else if (result.IsPermanent)
                                                {
                                                    updateTheEntity = FailLaunch(eventEntity, result, ref retryCount, ref resetRetries);
                                                }
                                                else
                                                {
                                                    RetryAfter(result);
                                                }
                                                break;
                                            }
                                        case InternalEventStatus.AppliedLaunch:
                                        case InternalEventStatus.AppliedRedeploy:
                                            {
                                                (casterApiClient, tokenResponse) = await RefreshClient(casterApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                var result = await CasterApiExtensions.WaitForRunToBeAppliedAsync(eventEntity, casterApiClient, _clientOptions.CurrentValue.CasterCheckIntervalSeconds, _clientOptions.CurrentValue.CasterDeployMaxWaitMinutes, _logger, ct);
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
                                                else if (result.IsPermanent)
                                                {
                                                    // Terraform failed to build the environment; the apply output is in the detail
                                                    updateTheEntity = FailLaunch(eventEntity, result, ref retryCount, ref resetRetries);
                                                }
                                                else
                                                {
                                                    // Apply timed out or hit a transient error, so run the whole plan again
                                                    lastTransientFailure = result;

                                                    switch (eventEntity.InternalStatus)
                                                    {
                                                        case InternalEventStatus.AppliedLaunch:
                                                            eventEntity.InternalStatus = InternalEventStatus.PlanningLaunch;
                                                            break;
                                                        case InternalEventStatus.AppliedRedeploy:
                                                            eventEntity.InternalStatus = InternalEventStatus.PlanningRedeploy;
                                                            break;
                                                    }

                                                    eventEntity.Status = EventStatus.Planning;
                                                    updateTheEntity = true;
                                                    retry = true;
                                                    resetRetries = false;
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
                                                else if (result.IsPermanent)
                                                {
                                                    updateTheEntity = FailLaunch(eventEntity, result, ref retryCount, ref resetRetries);
                                                }
                                                else
                                                {
                                                    RetryAfter(result);
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
                                        case InternalEventStatus.PlanningDestroy:
                                            {
                                                if (eventEntity.WorkspaceId != null)
                                                {
                                                    (casterApiClient, tokenResponse) = await RefreshClient(casterApiClient, tokenResponse, scope.ServiceProvider, ct);

                                                    var countResult = await CasterApiExtensions.GetWorkspaceResourceCountAsync(eventEntity, casterApiClient, _logger, ct);

                                                    if (!countResult.IsSuccess)
                                                    {
                                                        // Don't start a destroy run on a guess about what is in there.
                                                        updateTheEntity = RecordEndFailure(eventEntity, countResult);
                                                        RetryAfter(countResult);
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
                                                            updateTheEntity = RecordEndFailure(eventEntity, result);
                                                            RetryAfter(result);
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
                                                var result = await CasterApiExtensions.WaitForRunToBePlannedAsync(eventEntity, casterApiClient, _clientOptions.CurrentValue.CasterCheckIntervalSeconds, _clientOptions.CurrentValue.CasterPlanningMaxWaitMinutes, _logger, ct);
                                                if (result.IsSuccess)
                                                {
                                                    eventEntity.InternalStatus = InternalEventStatus.ApplyingDestroy;
                                                    updateTheEntity = true;
                                                }
                                                else
                                                {
                                                    // Destroy plan failed or timed out; plan it again from the top
                                                    RecordEndFailure(eventEntity, result);
                                                    lastTransientFailure = result;
                                                    eventEntity.InternalStatus = InternalEventStatus.PlanningDestroy;
                                                    updateTheEntity = true;
                                                    retry = true;
                                                    resetRetries = false;
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
                                                    updateTheEntity = RecordEndFailure(eventEntity, result);
                                                    RetryAfter(result);
                                                }
                                                break;
                                            }
                                        case InternalEventStatus.AppliedDestroy:
                                            {
                                                (casterApiClient, tokenResponse) = await RefreshClient(casterApiClient, tokenResponse, scope.ServiceProvider, ct);
                                                var applyResult = await CasterApiExtensions.WaitForRunToBeAppliedAsync(eventEntity, casterApiClient, _clientOptions.CurrentValue.CasterCheckIntervalSeconds, _clientOptions.CurrentValue.CasterDestroyMaxWaitMinutes, _logger, ct);
                                                if (!applyResult.IsSuccess)
                                                {
                                                    // Worth recording, but the resource count below stays the authority
                                                    // on whether the teardown actually got anywhere.
                                                    RecordEndFailure(eventEntity, applyResult);
                                                }

                                                // make sure that the run successfully deleted the resources
                                                var countResult = await CasterApiExtensions.GetWorkspaceResourceCountAsync(eventEntity, casterApiClient, _logger, ct);
                                                if (!countResult.IsSuccess)
                                                {
                                                    // Without a count there is nothing to judge progress by, so ask
                                                    // again rather than spend one of the destroy attempts.
                                                    updateTheEntity = RecordEndFailure(eventEntity, countResult);
                                                    RetryAfter(countResult);
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
                                                    updateTheEntity = RecordEndFailure(eventEntity, result);
                                                    RetryAfter(result);
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
                                                    updateTheEntity = RecordEndFailure(eventEntity, result);
                                                    RetryAfter(result);
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

                                                    // A launch that failed is routed through teardown to get its resources
                                                    // back, so reaching the end of teardown does not mean the Event ended
                                                    // normally. LastLaunchInternalStatus is only ever written when a launch
                                                    // failed - the enum starts at 1, so default means "never" - and it is
                                                    // cleared on every successful launch and redeploy. That makes it the
                                                    // honest signal here, with no extra column.
                                                    if (eventEntity.LastLaunchInternalStatus != default)
                                                    {
                                                        eventEntity.Status = EventStatus.Failed;
                                                        eventEntity.InternalStatus = InternalEventStatus.FailedLaunch;
                                                    }
                                                    else
                                                    {
                                                        eventEntity.Status = EventStatus.Ended;
                                                        eventEntity.InternalStatus = InternalEventStatus.Ended;

                                                        // Teardown that had to retry left a "Cleanup failed" note behind on
                                                        // its way here. It succeeded in the end, so drop it: leaving it set
                                                        // would brand a cleanly Ended Event as failed in the admin list for
                                                        // good, with nothing left running that could ever clear it.
                                                        eventEntity.ClearFailureState();
                                                    }
                                                    updateTheEntity = true;
                                                }
                                                else
                                                {
                                                    updateTheEntity = RecordEndFailure(eventEntity, result);
                                                    RetryAfter(result);
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
                        // FailLaunch moves Status to Ending, so decide before calling it.
                        var ending = eventEntity.Status == EventStatus.Ending;

                        if (result.IsPermanent && !ending)
                        {
                            updateTheEntity = FailLaunch(eventEntity, result, ref retryCount, ref resetRetries);
                        }
                        else
                        {
                            if (ending)
                            {
                                updateTheEntity = RecordEndFailure(eventEntity, result);
                            }

                            RetryAfter(result);
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

                            if ((eventEntity.Status == EventStatus.Creating ||
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

                            // An end request can be persisted while this launch step is
                            // saving, which overwrites the end status. Save first so that
                            // anything the launch just created is recorded, then pick the
                            // end request back up.
                            if (processingLaunch && await AdoptPendingEndAsync(alloyContext, eventEntity, ct))
                            {
                                // ending gets its own retry budget
                                retryCount = 0;
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

        /// <summary>
        /// Moves an Event that is launching or launched into the ending flow if an end
        /// request was persisted while this thread was working. EndDate is only ever set
        /// by an end or expiration request, so a launch state with an EndDate means an end
        /// request has not been acted on. Anything the launch already created stays on the
        /// Event so the ending flow can tear it down.
        /// </summary>
        private async Task<bool> AdoptPendingEndAsync(AlloyContext alloyContext, EventEntity eventEntity, CancellationToken ct)
        {
            if (eventEntity.Status != EventStatus.Creating &&
                eventEntity.Status != EventStatus.Planning &&
                eventEntity.Status != EventStatus.Applying &&
                eventEntity.Status != EventStatus.Active)
            {
                return false;
            }

            var endDate = await alloyContext.Events
                .AsNoTracking()
                .Where(x => x.Id == eventEntity.Id)
                .Select(x => x.EndDate)
                .FirstOrDefaultAsync(ct);

            if (endDate == null)
            {
                return false;
            }

            _logger.LogInformation("Event {EventId} was ended while it was in status {Status} - {InternalStatus}. Ending it.",
                eventEntity.Id, eventEntity.Status, eventEntity.InternalStatus);

            eventEntity.EndDate = endDate;
            eventEntity.Status = EventStatus.Ending;
            eventEntity.InternalStatus = InternalEventStatus.EndQueued;
            eventEntity.StatusDate = DateTime.UtcNow;
            await alloyContext.SaveChangesAsync(ct);

            return true;
        }

        // ---------------------------------------------------------------------------------------
        // Failure bookkeeping.
        //
        // EventEntity.ErrorMessage and EventEntity.ErrorDetail are written in exactly the four
        // helpers below, and cleared in exactly the four places that call ClearFailureState(). Nothing else
        // in the codebase assigns them, which is what makes the state reachable from a failure
        // reviewable: `grep -n 'ErrorMessage =' Alloy.Api/` should only ever find this block.
        //
        // Each helper returns the value to assign to updateTheEntity.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Marker separating the reason a launch failed from the reason its cleanup then failed.
        /// </summary>
        private const string CleanupFailureSeparator = "\n\nCleanup failed: ";

        /// <summary>
        /// Builds the result recorded when the retry budget runs out. The transient arms that got
        /// the Event here deliberately wrote nothing to it, so without the last failure folded in
        /// the detail would say only that we gave up - never what kept going wrong, which is the
        /// one question anyone reading it has. The user-facing <paramref name="summary"/> stays
        /// generic: the last transient summary ends in "retrying", which is no longer true.
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

            // Never EventStatus.Failed from here. Failed is outside ProcessTheEvent's loop
            // condition, so setting it at this point would abandon teardown and orphan the Player
            // View, Steamfitter Scenario and Caster Workspace that have already been created, with
            // nothing left to reclaim them. Hand off to the teardown states instead and let
            // DeletingScenario settle the terminal status.
            eventEntity.Status = EventStatus.Ending;
            eventEntity.InternalStatus = InternalEventStatus.EndQueued;

            // give teardown a full retry budget of its own
            retryCount = 0;
            resetRetries = true;

            return true;
        }

        /// <summary>
        /// Records why a teardown step failed, without giving up on it - only the retry ceiling
        /// stops teardown. Returns true only if there is something new to save, so a step that keeps
        /// failing the same way does not churn StatusDate and re-broadcast over SignalR every pass.
        /// </summary>
        private bool RecordEndFailure(EventEntity eventEntity, ApiCallResult result)
        {
            _logger.LogError("Cleanup of Event {EventId} failed at {Status} - {InternalStatus}: {Summary}",
                eventEntity.Id, eventEntity.Status, eventEntity.InternalStatus, result.Summary);

            // Drop any cleanup note left by an earlier pass before appending this one, so ten
            // retries don't append ten times and the original launch reason stays at the front.
            var launchReason = eventEntity.ErrorMessage ?? string.Empty;
            var separatorIndex = launchReason.IndexOf(CleanupFailureSeparator, StringComparison.Ordinal);
            if (separatorIndex >= 0)
            {
                launchReason = launchReason.Substring(0, separatorIndex);
            }

            var message = (string.IsNullOrEmpty(launchReason)
                    ? result.Summary
                    : launchReason + CleanupFailureSeparator + result.Summary)
                .Truncate(EventErrorLimits.MaxSummaryLength);

            if (message == eventEntity.ErrorMessage)
            {
                return false;
            }

            eventEntity.ErrorMessage = message;

            if (!string.IsNullOrEmpty(result.Detail))
            {
                eventEntity.ErrorDetail = result.Detail.StripAnsi().TruncateTail(EventErrorLimits.MaxDetailLength);
            }

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
        /// Handles a Status/InternalStatus combination the state machine does not know what to do
        /// with. On the launch side that means restarting teardown, which is safe because every
        /// teardown step skips what is already gone.
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
