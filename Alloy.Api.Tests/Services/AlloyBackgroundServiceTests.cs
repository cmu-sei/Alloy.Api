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

    /// <summary>The worker's host, for a principal of its own so each test chooses its options.</summary>
    private ApiTestHost Worker(Action<ApiTestHostOptions> configure = null) =>
        HostFor(new ClaimsPrincipalBuilder().Build(), configure ?? (_ => { }));

    private async Task Process(ApiTestHost worker, EventEntity entity)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        budget.CancelAfter(PassBudget);

        await worker.Resolve<AlloyBackgroundService>().ProcessEventAsync(entity, budget.Token).WaitAsync(budget.Token);
    }

    /// <summary>The end request as the endpoint makes it, through the host's own <c>EventService</c>.</summary>
    private async Task RequestEnd(ApiTestHost worker, Guid eventId)
    {
        using var scope = worker.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IEventService>().EndAsync(eventId, Ct);
    }

    private async Task<EventEntity> SeedEvent(EventStatus status, InternalEventStatus internalStatus, Action<EventEntity> configure = null)
    {
        var template = TestData.EventTemplate();
        var entity = TestData.Event(template.Id, status, internalStatus);
        configure?.Invoke(entity);
        await Seed(template, entity);

        return entity;
    }

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

    private CancellationToken CancellationToken => Ct;
}
