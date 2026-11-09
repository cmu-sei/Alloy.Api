// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Services;
using Alloy.Api.Tests.Support;
using Caster.Api.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Alloy.Api.Tests.Services;

/// <summary>
/// The launch and end worker, <see cref="AlloyBackgroundService.ProcessEventAsync"/>, driven directly over
/// the test's database with a scripted Player, Caster and Steamfitter. An end request can arrive at any
/// point of a launch, and whatever the worker had acquired by then must be released, or kept and reported
/// when it cannot be.
/// </summary>
public class AlloyBackgroundServiceTests(DatabaseFixture fixture) : ServiceTestBase(fixture)
{
    /// <summary>A pass of the worker either finishes or fails the test; the scripts never wait on a clock.</summary>
    private static readonly TimeSpan PassBudget = TimeSpan.FromSeconds(10);

    /// <summary>The grace period the grace-period tests configure, the shipped appsettings value.</summary>
    private const int GraceMinutes = 30;

    /// <summary>How the reason a suspended event's view is still visible begins.</summary>
    private const string ViewNotHidden = "The event was ended, but its Player view could not be hidden";

    /// <summary>How the reason a recovered event's view is still hidden begins.</summary>
    private const string ViewNotShown = "The event was recovered, but its Player view could not be made visible again";

    [Fact]
    public async Task A_state_save_completed_concurrently_does_not_tear_down_a_successful_launch()
    {
        var worker = Worker();
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Applied__State_Error };
        var entity = await SeedEvent(EventStatus.Applying, InternalEventStatus.AppliedLaunch, x =>
        {
            x.WorkspaceId = Guid.NewGuid();
            x.RunId = run.Id;
        });
        var saves = 0;
        worker.SiblingApis.Handle = request =>
        {
            if (request.RequestUri.AbsolutePath.EndsWith("/actions/save-state", StringComparison.Ordinal))
            {
                // Another caller saved the state after this poll, so Caster rejects this request.
                saves++;
                run.Status = RunStatus.Applied;
                return Task.FromResult(FakeSiblingApis.Json(new { error = "state already saved" }, HttpStatusCode.Conflict));
            }

            return Task.FromResult(FakeSiblingApis.Json(run));
        };

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal((EventStatus.Active, InternalEventStatus.Launched), (saved.Status, saved.InternalStatus));
        Assert.Equal((entity.WorkspaceId, run.Id), (saved.WorkspaceId, saved.RunId));
        Assert.Null(saved.EndDate);
        Assert.Null(saved.ErrorMessage);
        Assert.Equal(0, saved.FailureCount);
        Assert.Equal(1, saves);
        Assert.Equal(
            [Caster("GET", $"runs/{run.Id}"), Caster("POST", $"runs/{run.Id}/actions/save-state"), Caster("GET", $"runs/{run.Id}")],
            worker.SiblingApis.Urls);
    }

    /// <summary>The first read of the view fails transiently; the worker reads it again and writes it into the variables.</summary>
    [Fact]
    public async Task Workspace_preparation_reads_a_view_again_after_a_transient_failure()
    {
        var worker = Worker();
        var (entity, viewId) = await SeedWorkspacePreparation(hasView: true);
        var script = new PreparationScript(this, worker, entity.Id, viewId, HttpStatusCode.ServiceUnavailable);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal((2, 1, true, true), (script.ViewReads, script.WorkspaceCreates, script.WorkspaceDeleted, script.ViewDeleted));
        Assert.Equal(
            [
                Player("GET", $"views/{viewId}"), Player("GET", $"views/{viewId}"), Player("GET", $"views/{viewId}/teams"),
                Caster("POST", "workspaces"), Caster("POST", "files"),
                Caster("GET", $"workspaces/{script.WorkspaceId}/runs"), Caster("GET", $"workspaces/{script.WorkspaceId}/resources"),
                Caster("DELETE", $"workspaces/{script.WorkspaceId}"), Player("DELETE", $"views/{viewId}")
            ],
            worker.SiblingApis.Urls);
        Assert.Contains($"view_id = \"{viewId}\"", script.Variables);
        Assert.Equal(EventStatus.Ended, saved.Status);
        Assert.Equal((null, null), (saved.ViewId, saved.WorkspaceId));
        Assert.NotNull(saved.EndDate);
        Assert.Equal(default, saved.LastLaunchInternalStatus);
        Assert.Null(saved.ErrorMessage);
    }

    [Fact]
    public async Task Workspace_preparation_without_a_view_writes_empty_variables()
    {
        var worker = Worker();
        var (entity, viewId) = await SeedWorkspacePreparation(hasView: false);
        var script = new PreparationScript(this, worker, entity.Id, viewId, HttpStatusCode.OK);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal((0, 1, true, false), (script.ViewReads, script.WorkspaceCreates, script.WorkspaceDeleted, script.ViewDeleted));
        Assert.Equal(
            [
                Caster("POST", "workspaces"), Caster("POST", "files"),
                Caster("GET", $"workspaces/{script.WorkspaceId}/runs"), Caster("GET", $"workspaces/{script.WorkspaceId}/resources"),
                Caster("DELETE", $"workspaces/{script.WorkspaceId}")
            ],
            worker.SiblingApis.Urls);
        Assert.Equal(string.Empty, script.Variables);
        Assert.Equal(EventStatus.Ended, saved.Status);
        Assert.Equal((null, null), (saved.ViewId, saved.WorkspaceId));
        Assert.NotNull(saved.EndDate);
        Assert.Equal(default, saved.LastLaunchInternalStatus);
        Assert.Null(saved.ErrorMessage);
    }

    /// <summary>A view Player does not find is permanent: no workspace is created, the cloned view is deleted, the launch fails.</summary>
    [Fact]
    public async Task Workspace_preparation_fails_the_launch_when_the_view_is_not_found()
    {
        var worker = Worker();
        var (entity, viewId) = await SeedWorkspacePreparation(hasView: true);
        var script = new PreparationScript(this, worker, entity.Id, viewId, HttpStatusCode.NotFound);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal((1, 0, false, true), (script.ViewReads, script.WorkspaceCreates, script.WorkspaceDeleted, script.ViewDeleted));
        Assert.Equal([Player("GET", $"views/{viewId}"), Player("DELETE", $"views/{viewId}")], worker.SiblingApis.Urls);
        Assert.Null(script.Variables);
        Assert.Equal((EventStatus.Failed, InternalEventStatus.FailedLaunch), (saved.Status, saved.InternalStatus));
        Assert.Equal(InternalEventStatus.CreatingWorkspace, saved.LastLaunchInternalStatus);
        Assert.Contains("read the virtual environment configuration", saved.ErrorMessage);
        Assert.Equal((null, null), (saved.ViewId, saved.WorkspaceId));
        Assert.NotNull(saved.EndDate);
    }

    /// <summary>A request committed just before a restart (or a lost enqueue) is honoured before any more launch work.</summary>
    [Theory]
    [InlineData(EventStatus.Creating, InternalEventStatus.LaunchQueued)]
    [InlineData(EventStatus.Planning, InternalEventStatus.PlanningLaunch)]
    [InlineData(EventStatus.Applying, InternalEventStatus.StartingScenario)]
    [InlineData(EventStatus.Active, InternalEventStatus.Launched)]
    [InlineData(EventStatus.Paused, InternalEventStatus.Launched)]
    public async Task A_persisted_end_request_is_recovered_before_any_more_launch_work(EventStatus status, InternalEventStatus step)
    {
        var worker = Worker();
        var entity = await SeedEvent(status, step);
        await using (var request = NewContext())
        {
            await EventLifecycle.RequestEndAsync(request, await request.Events.SingleAsync(x => x.Id == entity.Id, Ct), DateTime.UtcNow, Ct);
        }

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal(EventStatus.Ended, saved.Status);
        Assert.True(saved.EndDate >= saved.EndRequestedAt);
        Assert.Null(saved.LaunchDate);
        Assert.Null(saved.ErrorMessage);
        Assert.Empty(worker.SiblingApis.Requests);
    }

    /// <summary>An end request during a Caster poll cancels the run, and the workspace is deleted only after it was rejected.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_end_during_Caster_polling_cancels_the_run_before_deleting_the_workspace(bool applying)
    {
        var worker = Worker();
        var run = new Run { Id = Guid.NewGuid(), Status = applying ? RunStatus.Applying : RunStatus.Planning };
        var entity = await SeedEvent(
            applying ? EventStatus.Applying : EventStatus.Planning,
            applying ? InternalEventStatus.AppliedLaunch : InternalEventStatus.PlannedLaunch,
            x =>
            {
                x.WorkspaceId = Guid.NewGuid();
                x.RunId = run.Id;
            });
        var statusesAtDelete = new List<RunStatus?>();
        var statusesAtResources = new List<RunStatus?>();
        var polls = new List<string>();
        var endSent = false;
        worker.SiblingApis.Handle = async request =>
        {
            var path = request.RequestUri.AbsolutePath.ToLowerInvariant();

            if (path.EndsWith("/actions/cancel", StringComparison.Ordinal))
            {
                run.Status = RunStatus.Rejected;
                return FakeSiblingApis.Json(run);
            }

            if (request.Method == HttpMethod.Delete)
            {
                statusesAtDelete.Add(run.Status);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (path.EndsWith("/resources", StringComparison.Ordinal))
            {
                statusesAtResources.Add(run.Status);
                return FakeSiblingApis.Json(Array.Empty<object>());
            }

            if (path.EndsWith("/runs", StringComparison.Ordinal))
            {
                return FakeSiblingApis.Json(new[] { run });
            }

            polls.Add(path);
            if (!endSent)
            {
                endSent = true;
                await RequestEnd(worker, entity.Id);
            }

            return FakeSiblingApis.Json(run);
        };

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.NotEmpty(statusesAtResources);
        Assert.All(statusesAtResources, x => Assert.Equal(RunStatus.Rejected, x));
        Assert.NotEmpty(statusesAtDelete);
        Assert.All(statusesAtDelete, x => Assert.Equal(RunStatus.Rejected, x));
        Assert.All(polls, x => Assert.Contains($"/runs/{run.Id}", x));
        Assert.Equal(EventStatus.Ended, saved.Status);
        Assert.Equal((null, null), (saved.WorkspaceId, saved.RunId));
        Assert.Null(saved.ErrorMessage);
        Assert.Equal(default, saved.LastLaunchInternalStatus);
        Assert.DoesNotContain(worker.SiblingApis.Requests, x => x.EndsWith("apply", StringComparison.Ordinal));
    }

    /// <summary>The scenario id is saved before cleanup, so the scenario a racing end interrupted is ended rather than orphaned.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_end_during_a_scenario_call_persists_its_id_before_cleanup(bool starting)
    {
        var worker = Worker();
        var scenarioId = Guid.NewGuid();
        var template = TestData.EventTemplate();
        template.ScenarioTemplateId = Guid.NewGuid();
        var entity = TestData.Event(
            template.Id,
            starting ? EventStatus.Applying : EventStatus.Creating,
            starting ? InternalEventStatus.StartingScenario : InternalEventStatus.CreatingScenario);
        entity.ScenarioId = starting ? scenarioId : null;
        await Seed(template, entity);
        var interruptedCall = starting
            ? $"PUT https://steamfitter.test/api/scenarios/{scenarioId}/start"
            : $"POST https://steamfitter.test/api/scenariotemplates/{template.ScenarioTemplateId}/scenarios";
        var scenarioIdsAtEnd = new List<Guid?>();
        var interrupted = new List<string>();
        worker.SiblingApis.Handle = async request =>
        {
            var path = request.RequestUri.AbsolutePath.ToLowerInvariant();

            if (path.EndsWith("/end", StringComparison.Ordinal))
            {
                scenarioIdsAtEnd.Add((await Stored(entity.Id)).ScenarioId);
                return FakeSiblingApis.Json(new { id = scenarioId });
            }

            interrupted.Add($"{request.Method} {request.RequestUri.GetLeftPart(UriPartial.Path)}");
            await RequestEnd(worker, entity.Id);
            return FakeSiblingApis.Json(new { id = scenarioId }, starting ? HttpStatusCode.OK : HttpStatusCode.Created);
        };

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal([interruptedCall], interrupted);
        Assert.NotEmpty(scenarioIdsAtEnd);
        Assert.All(scenarioIdsAtEnd, x => Assert.Equal(scenarioId, x));
        Assert.Equal(EventStatus.Ended, saved.Status);
        Assert.Null(saved.ScenarioId);
        Assert.NotNull(saved.EndDate);
        Assert.Null(saved.ErrorMessage);
    }

    [Fact]
    public async Task An_end_during_scenario_creation_never_starts_the_scenario()
    {
        var worker = Worker();
        var template = TestData.EventTemplate();
        template.ScenarioTemplateId = Guid.NewGuid();
        var entity = TestData.Event(template.Id, EventStatus.Creating, InternalEventStatus.CreatingScenario);
        await Seed(template, entity);
        worker.SiblingApis.Handle = async request =>
        {
            if (!request.RequestUri.AbsolutePath.EndsWith("/end", StringComparison.Ordinal))
            {
                await RequestEnd(worker, entity.Id);
            }

            return FakeSiblingApis.Json(new { id = Guid.NewGuid() }, HttpStatusCode.Created);
        };

        await Process(worker, entity);

        Assert.DoesNotContain(worker.SiblingApis.Requests, x => x.EndsWith("/start", StringComparison.Ordinal));
    }

    /// <summary>A run that never stops within the destroy budget keeps its workspace and run, and the event reports it.</summary>
    [Fact]
    public async Task A_cancellation_timeout_leaves_the_resources_and_reports_the_cleanup_failure()
    {
        var worker = Worker(o => o.ClientOptions.CasterDestroyMaxWaitMinutes = 0);
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Applying };
        var entity = await SeedEvent(EventStatus.Applying, InternalEventStatus.AppliedLaunch, x =>
        {
            x.WorkspaceId = Guid.NewGuid();
            x.RunId = run.Id;
        });
        await RequestEnd(worker, entity.Id);
        worker.SiblingApis.Handle = _ => Task.FromResult(FakeSiblingApis.Json(new[] { run }));

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal((EventStatus.Failed, InternalEventStatus.FailedDestroy), (saved.Status, saved.InternalStatus));
        Assert.Equal((entity.WorkspaceId, run.Id), (saved.WorkspaceId, saved.RunId));
        Assert.Null(saved.EndDate);
        Assert.Contains("did not stop", saved.ErrorMessage);
        Assert.Equal(default, saved.LastLaunchInternalStatus);
        Assert.All(worker.SiblingApis.Requests, x => Assert.StartsWith("GET ", x));
    }

    [Fact]
    public async Task The_cancellation_deadline_also_bounds_authentication_failures()
    {
        var worker = Worker(o =>
        {
            o.ClientOptions.CasterDestroyMaxWaitMinutes = 0;
            o.ClientOptions.ApiClientEndFailureMaxRetries = 0;
            o.ResourceOwnerAuthorization.Authority = "https://unreachable.test";
        });
        worker.SiblingApis.Handle = _ => throw new HttpRequestException("identity unavailable");
        var entity = await SeedEvent(EventStatus.Active, InternalEventStatus.Launched, x => x.WorkspaceId = Guid.NewGuid());
        await RequestEnd(worker, entity.Id);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal(EventStatus.Failed, saved.Status);
        Assert.NotNull(saved.WorkspaceId);
        Assert.Null(saved.EndDate);
        Assert.Contains("could not be confirmed stopped", saved.ErrorMessage);
    }

    /// <summary>Teardown state saved by an older version is reconciled once: the unrejected launch plan is rejected first.</summary>
    [Fact]
    public async Task An_old_cleanup_state_rejects_an_unrejected_launch_plan_before_deleting()
    {
        var worker = Worker();
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Planned };
        var entity = await SeedEvent(EventStatus.Ending, InternalEventStatus.DeletingWorkspace, x =>
        {
            x.WorkspaceId = Guid.NewGuid();
            x.RunId = run.Id;
        });
        var statusesAtDelete = new List<RunStatus?>();
        var statusesAtResources = new List<RunStatus?>();
        var deletes = new List<string>();
        worker.SiblingApis.Handle = request =>
        {
            var path = request.RequestUri.AbsolutePath;

            if (path.EndsWith("/actions/reject", StringComparison.Ordinal))
            {
                run.Status = RunStatus.Rejected;
                return Task.FromResult(FakeSiblingApis.Json(run));
            }

            if (path.EndsWith("/runs", StringComparison.Ordinal))
            {
                return Task.FromResult(FakeSiblingApis.Json(new[] { run }));
            }

            if (path.EndsWith("/resources", StringComparison.Ordinal))
            {
                statusesAtResources.Add(run.Status);
                return Task.FromResult(FakeSiblingApis.Json(Array.Empty<object>()));
            }

            statusesAtDelete.Add(run.Status);
            deletes.Add($"{request.Method} {request.RequestUri.GetLeftPart(UriPartial.Path)}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        };

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.NotEmpty(statusesAtResources);
        Assert.All(statusesAtResources, x => Assert.Equal(RunStatus.Rejected, x));
        Assert.Equal([RunStatus.Rejected], statusesAtDelete);
        Assert.Equal([Caster("DELETE", $"workspaces/{entity.WorkspaceId}")], deletes);
        Assert.Equal(EventStatus.Ended, saved.Status);
        Assert.Null(saved.WorkspaceId);
    }

    /// <summary>A destroy being recovered keeps its run across a lost poll response and is never cancelled or rejected.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_recovering_destroy_keeps_its_run_across_transient_poll_errors(bool applying)
    {
        var worker = Worker();
        var run = new Run { Id = Guid.NewGuid(), IsDestroy = true, Status = applying ? RunStatus.Applying : RunStatus.Planning };
        var entity = await SeedEvent(
            EventStatus.Ending,
            applying ? InternalEventStatus.AppliedDestroy : InternalEventStatus.PlannedDestroy,
            x =>
            {
                x.WorkspaceId = Guid.NewGuid();
                x.RunId = run.Id;
                x.EndRequestedAt = DateTime.UtcNow.AddMinutes(-1);
            });
        var reads = 0;
        var runIdsDuringPolls = new List<Guid?>();
        var polls = new List<string>();
        var statusesAtResources = new List<RunStatus?>();
        worker.SiblingApis.Handle = async request =>
        {
            var path = request.RequestUri.AbsolutePath;

            if (path.EndsWith("/runs", StringComparison.Ordinal))
            {
                return FakeSiblingApis.Json(new[] { run });
            }

            if (path.EndsWith("/resources", StringComparison.Ordinal))
            {
                statusesAtResources.Add(run.Status);
                return FakeSiblingApis.Json(Array.Empty<object>());
            }

            if (request.Method == HttpMethod.Delete)
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (path.EndsWith("/actions/apply", StringComparison.Ordinal))
            {
                run.Status = RunStatus.Applying;
                run.ApplyId = Guid.NewGuid();
                return FakeSiblingApis.Json(new Apply { Id = run.ApplyId.Value });
            }

            polls.Add(path);
            if (++reads == 1)
            {
                throw new HttpRequestException("poll response lost");
            }

            runIdsDuringPolls.Add((await Stored(entity.Id)).RunId);
            run.Status = run.Status == RunStatus.Planning ? RunStatus.Planned
                : run.Status == RunStatus.Applying ? RunStatus.Applied : run.Status;
            return FakeSiblingApis.Json(run);
        };

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.NotEmpty(statusesAtResources);
        Assert.All(statusesAtResources, x => Assert.Equal(RunStatus.Applied, x));
        Assert.All(polls, x => Assert.Contains($"/runs/{run.Id}", x));
        Assert.All(runIdsDuringPolls, x => Assert.Equal(run.Id, x));
        Assert.Equal(EventStatus.Ended, saved.Status);
        Assert.Null(saved.RunId);
        Assert.Null(saved.ErrorMessage);
        Assert.True(reads >= 2);
        Assert.DoesNotContain(worker.SiblingApis.Requests, x => x.Contains("cancel", StringComparison.Ordinal) || x.Contains("reject", StringComparison.Ordinal));
    }

    /// <summary>Retrying a failed cleanup that has nothing left to release keeps the original launch failure on the event.</summary>
    [Fact]
    public async Task An_explicit_retry_of_a_failed_cleanup_preserves_the_launch_failure()
    {
        var worker = Worker();
        var entity = await SeedEvent(EventStatus.Failed, InternalEventStatus.FailedDestroy, x =>
        {
            x.LastLaunchInternalStatus = InternalEventStatus.CreatingWorkspace;
            x.ErrorMessage = "The original launch failed.";
            x.EndRequestedAt = DateTime.UtcNow.AddHours(-1);
        });
        await RequestEnd(worker, entity.Id);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal((EventStatus.Failed, InternalEventStatus.FailedLaunch), (saved.Status, saved.InternalStatus));
        Assert.Equal("The original launch failed.", saved.ErrorMessage);
        Assert.NotNull(saved.EndDate);
    }

    /// <summary>A notification read before another worker failed the cleanup does not restart it.</summary>
    [Fact]
    public async Task A_stale_recovery_notification_cannot_restart_a_failed_cleanup()
    {
        var worker = Worker();
        var notification = await SeedEvent(EventStatus.Ending, InternalEventStatus.EndQueued);
        await using (var other = NewContext())
        {
            var row = await other.Events.SingleAsync(x => x.Id == notification.Id, Ct);
            row.Status = EventStatus.Failed;
            row.InternalStatus = InternalEventStatus.FailedDestroy;
            row.EndRequestedAt = DateTime.UtcNow;
            row.FailureCount = 2;
            await other.SaveChangesAsync(Ct);
        }

        await Process(worker, notification);

        var saved = await Stored(notification.Id);
        Assert.Equal(EventStatus.Failed, saved.Status);
        Assert.Equal(2, saved.FailureCount);
        Assert.Null(saved.EndDate);
    }

    /// <summary>
    /// With a grace period, an end only hides the view: Player is asked for it and told to set it Inactive with
    /// its other fields as they were, and the workspace, run and scenario stay, so a recover has them to give back.
    /// </summary>
    [Fact]
    public async Task An_end_with_a_grace_period_suspends_the_event_and_only_hides_its_view()
    {
        var worker = GraceWorker();
        var entity = await SeedLaunchedEvent();
        var view = new ViewScript(worker, entity.ViewId.Value, "Active", Ct);
        await RequestEnd(worker, entity.Id);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal([Player("GET", $"views/{entity.ViewId}"), Player("PUT", $"views/{entity.ViewId}")], worker.SiblingApis.Urls);
        Assert.Equal(["Inactive"], view.PutStatuses);
        Assert.Equal([ViewScript.Name], view.PutNames);
        Assert.Equal((EventStatus.Suspended, InternalEventStatus.Suspended), (saved.Status, saved.InternalStatus));
        Assert.NotNull(saved.EndRequestedAt);
        Assert.Equal(saved.EndRequestedAt.Value.AddMinutes(GraceMinutes), saved.RecoverableUntil);
        Assert.Equal((entity.ViewId, entity.WorkspaceId, entity.RunId, entity.ScenarioId), (saved.ViewId, saved.WorkspaceId, saved.RunId, saved.ScenarioId));
        Assert.Null(saved.EndDate);
        Assert.Null(saved.ErrorMessage);
    }

    [Fact]
    public async Task An_end_with_a_grace_period_does_not_update_a_view_that_is_already_hidden()
    {
        var worker = GraceWorker();
        var entity = await SeedLaunchedEvent();
        var view = new ViewScript(worker, entity.ViewId.Value, "Inactive", Ct);
        await RequestEnd(worker, entity.Id);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal([Player("GET", $"views/{entity.ViewId}")], worker.SiblingApis.Urls);
        Assert.Empty(view.PutStatuses);
        Assert.Equal(EventStatus.Suspended, saved.Status);
    }

    /// <summary>A view Player no longer has cannot be listed, so it counts as hidden and records no failure.</summary>
    [Fact]
    public async Task An_end_with_a_grace_period_counts_a_view_Player_no_longer_has_as_hidden()
    {
        var worker = GraceWorker();
        var entity = await SeedLaunchedEvent();
        _ = new ViewScript(worker, entity.ViewId.Value, "Active", Ct) { GetFailure = HttpStatusCode.NotFound };
        await RequestEnd(worker, entity.Id);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal([Player("GET", $"views/{entity.ViewId}")], worker.SiblingApis.Urls);
        Assert.Equal((EventStatus.Suspended, InternalEventStatus.Suspended), (saved.Status, saved.InternalStatus));
        Assert.Null(saved.ErrorMessage);
        Assert.Equal(entity.WorkspaceId, saved.WorkspaceId);
    }

    [Fact]
    public async Task An_end_with_a_grace_period_suspends_an_event_without_a_view_without_asking_Player()
    {
        var worker = GraceWorker();
        var entity = await SeedLaunchedEvent(x => x.ViewId = null);
        await RequestEnd(worker, entity.Id);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Empty(worker.SiblingApis.Requests);
        Assert.Equal((EventStatus.Suspended, InternalEventStatus.Suspended), (saved.Status, saved.InternalStatus));
        Assert.Equal(entity.WorkspaceId, saved.WorkspaceId);
    }

    /// <summary>A paused event is suspended like an active one.</summary>
    [Fact]
    public async Task An_end_with_a_grace_period_suspends_a_paused_event()
    {
        var worker = GraceWorker();
        var entity = await SeedLaunchedEvent(x => x.Status = EventStatus.Paused);
        _ = new ViewScript(worker, entity.ViewId.Value, "Active", Ct);
        await RequestEnd(worker, entity.Id);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal((EventStatus.Suspended, InternalEventStatus.Suspended), (saved.Status, saved.InternalStatus));
        Assert.Equal(entity.WorkspaceId, saved.WorkspaceId);
    }

    /// <summary>
    /// Player refusing the update is permanent, but nothing has been destroyed, so the event is still suspended
    /// and recoverable; the reason is kept for its owner.
    /// </summary>
    [Fact]
    public async Task A_permanent_Player_failure_hiding_the_view_still_suspends_the_event_with_the_reason()
    {
        var worker = GraceWorker();
        var entity = await SeedLaunchedEvent();
        _ = new ViewScript(worker, entity.ViewId.Value, "Active", Ct) { PutFailure = HttpStatusCode.BadRequest };
        await RequestEnd(worker, entity.Id);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal([Player("GET", $"views/{entity.ViewId}"), Player("PUT", $"views/{entity.ViewId}")], worker.SiblingApis.Urls);
        Assert.Equal((EventStatus.Suspended, InternalEventStatus.Suspended), (saved.Status, saved.InternalStatus));
        Assert.StartsWith(ViewNotHidden, saved.ErrorMessage);
        Assert.Contains("HTTP 400", saved.ErrorMessage);
        Assert.Equal(saved.EndRequestedAt.Value.AddMinutes(GraceMinutes), saved.RecoverableUntil);
        Assert.Equal((entity.ViewId, entity.WorkspaceId, entity.ScenarioId), (saved.ViewId, saved.WorkspaceId, saved.ScenarioId));
        Assert.Null(saved.EndDate);
        Assert.Equal(0, saved.FailureCount);
    }

    /// <summary>
    /// A Player that keeps failing transiently is given up on at the end retry ceiling (two attempts here), and
    /// the event is suspended with the reason, never failed or torn down.
    /// </summary>
    [Fact]
    public async Task Hiding_the_view_gives_up_at_the_end_retry_ceiling_and_still_suspends_the_event()
    {
        var worker = GraceWorker();
        var entity = await SeedLaunchedEvent();
        _ = new ViewScript(worker, entity.ViewId.Value, "Active", Ct) { GetFailure = HttpStatusCode.ServiceUnavailable };
        await RequestEnd(worker, entity.Id);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal([Player("GET", $"views/{entity.ViewId}"), Player("GET", $"views/{entity.ViewId}")], worker.SiblingApis.Urls);
        Assert.Equal((EventStatus.Suspended, InternalEventStatus.Suspended), (saved.Status, saved.InternalStatus));
        Assert.StartsWith(ViewNotHidden, saved.ErrorMessage);
        Assert.Contains("Player could not be reached.", saved.ErrorMessage);
        Assert.Contains("Gave up after 2 attempts", saved.ErrorDetail);
        Assert.Equal((entity.ViewId, entity.WorkspaceId, entity.ScenarioId), (saved.ViewId, saved.WorkspaceId, saved.ScenarioId));
        Assert.Null(saved.EndDate);
    }

    /// <summary>Without a grace period (the option's default, 0) an end destroys at once and records no grace.</summary>
    [Fact]
    public async Task Without_a_grace_period_an_end_tears_the_event_down_at_once()
    {
        var worker = Worker(o => o.Resource.EndGracePeriodMinutes = 0);
        var entity = await SeedLaunchedEvent(x =>
        {
            x.WorkspaceId = null;
            x.RunId = null;
            x.ScenarioId = null;
        });
        worker.SiblingApis.Handle = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        await RequestEnd(worker, entity.Id);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal([Player("DELETE", $"views/{entity.ViewId}")], worker.SiblingApis.Urls);
        Assert.Equal((EventStatus.Ended, InternalEventStatus.Ended), (saved.Status, saved.InternalStatus));
        Assert.Null(saved.ViewId);
        Assert.Null(saved.RecoverableUntil);
        Assert.NotNull(saved.EndDate);
    }

    /// <summary>An end during a launch has no stable state to give back, so it tears down at once whatever the grace.</summary>
    [Theory]
    [InlineData(EventStatus.Creating, InternalEventStatus.LaunchQueued)]
    [InlineData(EventStatus.Planning, InternalEventStatus.PlanningLaunch)]
    [InlineData(EventStatus.Applying, InternalEventStatus.StartingScenario)]
    public async Task An_end_during_a_launch_tears_down_at_once_even_with_a_grace_period(EventStatus status, InternalEventStatus step)
    {
        var worker = GraceWorker();
        var entity = await SeedEvent(status, step);
        await RequestEnd(worker, entity.Id);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal(EventStatus.Ended, saved.Status);
        Assert.Null(saved.RecoverableUntil);
        Assert.NotNull(saved.EndDate);
        Assert.Empty(worker.SiblingApis.Requests);
    }

    /// <summary>A stale notification for a suspension inside its grace changes nothing and calls nothing.</summary>
    [Fact]
    public async Task A_suspended_event_inside_its_grace_is_left_as_it_is()
    {
        var worker = GraceWorker();
        var recoverableUntil = DateTime.UtcNow.AddMinutes(GraceMinutes).Date.AddDays(1);
        var entity = await SeedLaunchedEvent(x =>
        {
            x.Status = EventStatus.Suspended;
            x.InternalStatus = InternalEventStatus.Suspended;
            x.EndRequestedAt = TestData.DefaultDate;
            x.RecoverableUntil = recoverableUntil;
        });

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Empty(worker.SiblingApis.Requests);
        Assert.Equal((EventStatus.Suspended, InternalEventStatus.Suspended), (saved.Status, saved.InternalStatus));
        Assert.Equal(recoverableUntil, saved.RecoverableUntil);
        Assert.Equal(TestData.DefaultDate, saved.StatusDate);
        Assert.Equal((entity.ViewId, entity.WorkspaceId, entity.ScenarioId), (saved.ViewId, saved.WorkspaceId, saved.ScenarioId));
    }

    /// <summary>
    /// Once the grace has passed the suspension is torn down as any end is: the workspace is deleted, then the
    /// view, then the scenario ended, and the event is Ended. The view is never made visible on the way.
    /// </summary>
    [Fact]
    public async Task A_suspended_event_whose_grace_has_passed_is_torn_down_to_Ended()
    {
        var worker = GraceWorker();
        var entity = await SeedLaunchedEvent(x =>
        {
            x.Status = EventStatus.Suspended;
            x.InternalStatus = InternalEventStatus.Suspended;
            x.RunId = null;
            x.EndRequestedAt = DateTime.UtcNow.AddMinutes(-GraceMinutes - 1);
            x.RecoverableUntil = DateTime.UtcNow.AddMinutes(-1);
        });
        ScriptTeardown(worker, entity);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal(Teardown(entity), worker.SiblingApis.Urls);
        Assert.Equal((EventStatus.Ended, InternalEventStatus.Ended), (saved.Status, saved.InternalStatus));
        Assert.Equal((null, null, null), (saved.ViewId, saved.WorkspaceId, saved.ScenarioId));
        Assert.NotNull(saved.EndDate);
        Assert.Null(saved.ErrorMessage);
    }

    /// <summary>An administrator's immediate end skips the grace period: the view is deleted with the rest, never hidden first.</summary>
    [Fact]
    public async Task An_immediate_end_tears_an_active_event_down_without_hiding_its_view()
    {
        var worker = GraceWorker();
        var entity = await SeedLaunchedEvent(x => x.RunId = null);
        ScriptTeardown(worker, entity);
        await RequestEnd(worker, entity.Id, immediate: true);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal(Teardown(entity), worker.SiblingApis.Urls);
        Assert.Equal((EventStatus.Ended, InternalEventStatus.Ended), (saved.Status, saved.InternalStatus));
        Assert.Equal((null, null, null), (saved.ViewId, saved.WorkspaceId, saved.ScenarioId));
        Assert.NotNull(saved.EndDate);
        Assert.NotNull(saved.EndRequestedAt);
        Assert.Equal(saved.EndRequestedAt, saved.RecoverableUntil);
        Assert.Null(saved.ErrorMessage);
    }

    /// <summary>An immediate end of an event already waiting out its grace period tears it down now.</summary>
    [Fact]
    public async Task An_immediate_end_of_a_suspended_event_inside_its_grace_tears_it_down()
    {
        var worker = GraceWorker();
        var entity = await SeedLaunchedEvent(x =>
        {
            x.Status = EventStatus.Suspended;
            x.InternalStatus = InternalEventStatus.Suspended;
            x.RunId = null;
            x.EndRequestedAt = DateTime.UtcNow.AddMinutes(-1);
            x.RecoverableUntil = DateTime.UtcNow.AddMinutes(GraceMinutes - 1);
        });
        ScriptTeardown(worker, entity);
        await RequestEnd(worker, entity.Id, immediate: true);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal(Teardown(entity), worker.SiblingApis.Urls);
        Assert.Equal((EventStatus.Ended, InternalEventStatus.Ended), (saved.Status, saved.InternalStatus));
        Assert.Equal((null, null, null), (saved.ViewId, saved.WorkspaceId, saved.ScenarioId));
        Assert.NotNull(saved.EndDate);
        Assert.True(saved.RecoverableUntil <= DateTime.UtcNow);
    }

    /// <summary>An immediate end that lands while the view is still being hidden skips the hiding and tears down.</summary>
    [Fact]
    public async Task An_immediate_end_while_the_view_is_being_hidden_skips_hiding_it_and_tears_down()
    {
        var worker = GraceWorker();
        var entity = await SeedLaunchedEvent(x =>
        {
            x.Status = EventStatus.Ending;
            x.InternalStatus = InternalEventStatus.SuspendingView;
            x.RunId = null;
            x.EndRequestedAt = DateTime.UtcNow.AddMinutes(-1);
            x.RecoverableUntil = DateTime.UtcNow.AddMinutes(GraceMinutes - 1);
        });
        ScriptTeardown(worker, entity);
        await RequestEnd(worker, entity.Id, immediate: true);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal(Teardown(entity), worker.SiblingApis.Urls);
        Assert.Equal((EventStatus.Ended, InternalEventStatus.Ended), (saved.Status, saved.InternalStatus));
        Assert.Equal((null, null, null), (saved.ViewId, saved.WorkspaceId, saved.ScenarioId));
        Assert.NotNull(saved.EndDate);
    }

    /// <summary>A recover makes the view visible again with its other fields as they were, and the event Active.</summary>
    [Fact]
    public async Task Recovery_makes_the_view_visible_again_and_the_event_Active()
    {
        var worker = GraceWorker();
        var entity = await SeedRecovery();
        var view = new ViewScript(worker, entity.ViewId.Value, "Inactive", Ct);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal([Player("GET", $"views/{entity.ViewId}"), Player("PUT", $"views/{entity.ViewId}")], worker.SiblingApis.Urls);
        Assert.Equal(["Active"], view.PutStatuses);
        Assert.Equal([ViewScript.Name], view.PutNames);
        Assert.Equal((EventStatus.Active, InternalEventStatus.Launched), (saved.Status, saved.InternalStatus));
        Assert.Null(saved.ErrorMessage);
        Assert.Equal((entity.ViewId, entity.WorkspaceId, entity.ScenarioId), (saved.ViewId, saved.WorkspaceId, saved.ScenarioId));
        Assert.Null(saved.EndDate);
    }

    /// <summary>
    /// A view that is gone cannot be shown again; the event keeps its resources and is Active, with the reason,
    /// rather than being torn down.
    /// </summary>
    [Fact]
    public async Task A_permanent_Player_failure_during_recovery_leaves_the_event_Active_with_the_reason()
    {
        var worker = GraceWorker();
        var entity = await SeedRecovery();
        _ = new ViewScript(worker, entity.ViewId.Value, "Inactive", Ct) { GetFailure = HttpStatusCode.NotFound };

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal([Player("GET", $"views/{entity.ViewId}")], worker.SiblingApis.Urls);
        Assert.Equal((EventStatus.Active, InternalEventStatus.Launched), (saved.Status, saved.InternalStatus));
        Assert.StartsWith(ViewNotShown, saved.ErrorMessage);
        Assert.Contains("HTTP 404", saved.ErrorMessage);
        Assert.Equal((entity.ViewId, entity.WorkspaceId, entity.ScenarioId), (saved.ViewId, saved.WorkspaceId, saved.ScenarioId));
        Assert.Null(saved.EndDate);
        Assert.Equal(0, saved.FailureCount);
    }

    /// <summary>A Player that keeps failing transiently is given up on at the launch retry ceiling, leaving the event Active.</summary>
    [Fact]
    public async Task Recovery_gives_up_at_the_launch_retry_ceiling_and_leaves_the_event_Active()
    {
        var worker = GraceWorker();
        var entity = await SeedRecovery();
        _ = new ViewScript(worker, entity.ViewId.Value, "Inactive", Ct) { GetFailure = HttpStatusCode.ServiceUnavailable };

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal([Player("GET", $"views/{entity.ViewId}"), Player("GET", $"views/{entity.ViewId}")], worker.SiblingApis.Urls);
        Assert.Equal((EventStatus.Active, InternalEventStatus.Launched), (saved.Status, saved.InternalStatus));
        Assert.StartsWith(ViewNotShown, saved.ErrorMessage);
        Assert.Contains("Player could not be reached.", saved.ErrorMessage);
        Assert.Equal(entity.WorkspaceId, saved.WorkspaceId);
    }

    /// <summary>An end requested while the view is being shown again is honoured: the event is suspended again, with a new grace.</summary>
    [Fact]
    public async Task An_end_during_recovery_suspends_the_event_again()
    {
        var worker = GraceWorker();
        var entity = await SeedRecovery();
        var view = new ViewScript(worker, entity.ViewId.Value, "Inactive", Ct);
        view.OnFirstPut = () => RequestEnd(worker, entity.Id);

        await Process(worker, entity);

        var saved = await Stored(entity.Id);
        Assert.Equal(
            [
                Player("GET", $"views/{entity.ViewId}"), Player("PUT", $"views/{entity.ViewId}"),
                Player("GET", $"views/{entity.ViewId}"), Player("PUT", $"views/{entity.ViewId}")
            ],
            worker.SiblingApis.Urls);
        Assert.Equal(["Active", "Inactive"], view.PutStatuses);
        Assert.Equal((EventStatus.Suspended, InternalEventStatus.Suspended), (saved.Status, saved.InternalStatus));
        Assert.NotNull(saved.EndRequestedAt);
        Assert.Equal(saved.EndRequestedAt.Value.AddMinutes(GraceMinutes), saved.RecoverableUntil);
        Assert.Equal((entity.ViewId, entity.WorkspaceId, entity.ScenarioId), (saved.ViewId, saved.WorkspaceId, saved.ScenarioId));
        Assert.Null(saved.EndDate);
    }

    /// <summary>The worker's host, for a principal of its own so each test chooses its options.</summary>
    private ApiTestHost Worker(Action<ApiTestHostOptions> configure = null) =>
        HostFor(new ClaimsPrincipalBuilder().Build(), configure ?? (_ => { }));

    /// <summary>A worker with the grace period of <see cref="GraceMinutes"/>.</summary>
    private ApiTestHost GraceWorker() => Worker(o => o.Resource.EndGracePeriodMinutes = GraceMinutes);

    private async Task Process(ApiTestHost worker, EventEntity entity)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        budget.CancelAfter(PassBudget);

        await worker.Resolve<AlloyBackgroundService>().ProcessEventAsync(entity, budget.Token).WaitAsync(budget.Token);
    }

    /// <summary>The end request as the endpoint makes it, through the host's own <c>EventService</c>.</summary>
    private async Task RequestEnd(ApiTestHost worker, Guid eventId, bool immediate = false)
    {
        using var scope = worker.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IEventService>().EndAsync(eventId, Ct, immediate);
    }

    /// <summary>
    /// Caster, Player and Steamfitter for a teardown with nothing in the workspace: the run and resource lists are
    /// empty, deletes succeed, and the scenario end answers the scenario.
    /// </summary>
    private static void ScriptTeardown(ApiTestHost worker, EventEntity entity) =>
        worker.SiblingApis.Handle = request => Task.FromResult(
            request.Method == HttpMethod.Get ? FakeSiblingApis.Json(Array.Empty<object>())
            : request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : FakeSiblingApis.Json(new { id = entity.ScenarioId }));

    /// <summary>The requests of a teardown in order: the workspace, then the view, then the scenario. No view is ever updated.</summary>
    private static string[] Teardown(EventEntity entity) =>
    [
        Caster("GET", $"workspaces/{entity.WorkspaceId}/runs"), Caster("GET", $"workspaces/{entity.WorkspaceId}/resources"),
        Caster("DELETE", $"workspaces/{entity.WorkspaceId}"), Player("DELETE", $"views/{entity.ViewId}"),
        $"PUT https://steamfitter.test/api/scenarios/{entity.ScenarioId}/end"
    ];

    private async Task<EventEntity> SeedEvent(EventStatus status, InternalEventStatus internalStatus, Action<EventEntity> configure = null)
    {
        var template = TestData.EventTemplate();
        var entity = TestData.Event(template.Id, status, internalStatus);
        configure?.Invoke(entity);
        await Seed(template, entity);

        return entity;
    }

    /// <summary>An Active event with a view, a workspace, its launch run and a scenario.</summary>
    private Task<EventEntity> SeedLaunchedEvent(Action<EventEntity> configure = null) =>
        SeedEvent(EventStatus.Active, InternalEventStatus.Launched, x =>
        {
            x.ViewId = Guid.NewGuid();
            x.WorkspaceId = Guid.NewGuid();
            x.RunId = Guid.NewGuid();
            x.ScenarioId = Guid.NewGuid();
            configure?.Invoke(x);
        });

    /// <summary>A suspended event a recover has just handed back to the worker, as <c>EventLifecycle.RecoverAsync</c> leaves it.</summary>
    private Task<EventEntity> SeedRecovery() =>
        SeedLaunchedEvent(x =>
        {
            x.Status = EventStatus.Recovering;
            x.InternalStatus = InternalEventStatus.ResumingView;
        });

    private async Task<(EventEntity Entity, Guid ViewId)> SeedWorkspacePreparation(bool hasView)
    {
        var viewId = Guid.NewGuid();
        var template = TestData.EventTemplate();
        template.DirectoryId = Guid.NewGuid();
        var entity = TestData.Event(template.Id, EventStatus.Creating, InternalEventStatus.CreatingWorkspace);
        entity.ViewId = hasView ? viewId : null;
        await Seed(template, entity);

        return (entity, viewId);
    }

    /// <summary>A request to Player, as <see cref="FakeSiblingApis.Urls"/> lists it, at the url <c>ApiTestHostOptions</c> configures.</summary>
    private static string Player(string method, string route) => $"{method} https://player.test/api/{route}";

    /// <summary>A request to Caster, as <see cref="FakeSiblingApis.Urls"/> lists it, at the url <c>ApiTestHostOptions</c> configures.</summary>
    private static string Caster(string method, string route) => $"{method} https://caster.test/api/{route}";

    private async Task<EventEntity> Stored(Guid id)
    {
        await using var context = NewContext();

        return await context.Events.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }

    /// <summary>
    /// Player and Caster for workspace preparation: the first read of the view answers
    /// <c>firstViewRead</c>, the variables file is captured, and an end is requested once it is written,
    /// to isolate preparation from launch polling.
    /// </summary>
    private sealed class PreparationScript
    {
        public PreparationScript(AlloyBackgroundServiceTests test, ApiTestHost worker, Guid eventId, Guid viewId, HttpStatusCode firstViewRead)
        {
            var workspaceId = WorkspaceId;

            worker.SiblingApis.Handle = async request =>
            {
                var path = request.RequestUri.AbsolutePath.ToLowerInvariant();

                var view = $"/api/views/{viewId}";
                var workspace = $"/api/workspaces/{workspaceId}";

                if (request.RequestUri.Host == "player.test")
                {
                    if (request.Method == HttpMethod.Delete && path == view)
                    {
                        ViewDeleted = true;
                        return new HttpResponseMessage(HttpStatusCode.NoContent);
                    }

                    if (request.Method == HttpMethod.Get && path.EndsWith("/teams", StringComparison.Ordinal))
                    {
                        return FakeSiblingApis.Json(Array.Empty<object>());
                    }

                    if (request.Method == HttpMethod.Get && path == view)
                    {
                        return ++ViewReads == 1
                            ? FakeSiblingApis.Json(new { message = "Configuration unavailable" }, firstViewRead)
                            : FakeSiblingApis.Json(new { id = viewId });
                    }

                    throw new InvalidOperationException($"Unexpected Player request: {request.Method} {path}");
                }

                if (request.Method == HttpMethod.Post && path.EndsWith("/workspaces", StringComparison.Ordinal))
                {
                    WorkspaceCreates++;
                    return FakeSiblingApis.Json(new { id = workspaceId }, HttpStatusCode.Created);
                }

                if (request.Method == HttpMethod.Post && path.EndsWith("/files", StringComparison.Ordinal))
                {
                    using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(test.CancellationToken));
                    Variables = body.RootElement.GetProperty("content").GetString();
                    await test.RequestEnd(worker, eventId);
                    return FakeSiblingApis.Json(new { id = Guid.NewGuid() }, HttpStatusCode.Created);
                }

                if (request.Method == HttpMethod.Get && (path.EndsWith("/runs", StringComparison.Ordinal) || path.EndsWith("/resources", StringComparison.Ordinal)))
                {
                    return FakeSiblingApis.Json(Array.Empty<object>());
                }

                if (request.Method == HttpMethod.Delete && path == workspace)
                {
                    WorkspaceDeleted = true;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }

                throw new InvalidOperationException($"Unexpected Caster request: {request.Method} {path}");
            };
        }

        /// <summary>The id Caster answers the workspace create with.</summary>
        public Guid WorkspaceId { get; } = Guid.NewGuid();

        public int ViewReads { get; private set; }

        public int WorkspaceCreates { get; private set; }

        public bool ViewDeleted { get; private set; }

        public bool WorkspaceDeleted { get; private set; }

        public string Variables { get; private set; }
    }

    /// <summary>
    /// Player's view for hiding and showing it: a GET answers the view with its current status, a PUT records
    /// the status and name it sends and takes the status on. A failure answers every GET or PUT with that status.
    /// </summary>
    private sealed class ViewScript
    {
        /// <summary>The view's name, which an update has to send back unchanged.</summary>
        public const string Name = "Cloned lab view";

        public ViewScript(ApiTestHost worker, Guid viewId, string status, CancellationToken ct)
        {
            Status = status;
            worker.SiblingApis.Handle = async request =>
            {
                if (request.RequestUri.Host != "player.test" ||
                    request.RequestUri.AbsolutePath != $"/api/views/{viewId}")
                {
                    throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
                }

                if (request.Method == HttpMethod.Get)
                {
                    return GetFailure is { } getFailure
                        ? FakeSiblingApis.Json(new { message = "Player failed" }, getFailure)
                        : FakeSiblingApis.Json(View(viewId));
                }

                if (request.Method == HttpMethod.Put)
                {
                    using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
                    var sent = body.RootElement.GetProperty("status").GetString();
                    PutStatuses.Add(sent);
                    PutNames.Add(body.RootElement.GetProperty("name").GetString());
                    if (PutFailure is { } putFailure)
                    {
                        return FakeSiblingApis.Json(new { message = "Player failed" }, putFailure);
                    }

                    if (OnFirstPut is { } onFirstPut)
                    {
                        OnFirstPut = null;
                        await onFirstPut();
                    }

                    Status = sent;
                    return FakeSiblingApis.Json(View(viewId));
                }

                throw new InvalidOperationException($"Unexpected Player request: {request.Method} {request.RequestUri}");
            };
        }

        public string Status { get; private set; }

        public HttpStatusCode? GetFailure { get; init; }

        public HttpStatusCode? PutFailure { get; init; }

        /// <summary>Run while the first update is in flight, after Player received it.</summary>
        public Func<Task> OnFirstPut { get; set; }

        public List<string> PutStatuses { get; } = [];

        public List<string> PutNames { get; } = [];

        private object View(Guid viewId) => new { id = viewId, name = Name, description = "A lab", status = Status, isTemplate = false };
    }

    private CancellationToken CancellationToken => Ct;
}
