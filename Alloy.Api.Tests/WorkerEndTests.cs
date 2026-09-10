using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Infrastructure.Options;
using Alloy.Api.Services;
using Caster.Api.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alloy.Api.Tests;

[Collection("Postgres")]
public class WorkerEndTests(PostgresFixture postgres)
{
    private static async Task Process(TestEnvironment env, EventEntity entity)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await env.Worker().ProcessEventAsync(entity, timeout.Token).WaitAsync(timeout.Token);
    }

    [Theory]
    [InlineData(false, HttpStatusCode.OK)]
    [InlineData(true, HttpStatusCode.ServiceUnavailable)]
    [InlineData(true, HttpStatusCode.NotFound)]
    public async Task WorkspacePreparationHandlesMissingAndFailingPlayerViews(bool hasView, HttpStatusCode firstReadStatus)
    {
        using var env = new TestEnvironment(postgres);
        var entity = await env.Seed(EventStatus.Creating, InternalEventStatus.CreatingWorkspace);
        var viewId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        using (var db = env.Context())
        {
            (await db.EventTemplates.SingleAsync()).DirectoryId = Guid.NewGuid();
            (await db.Events.SingleAsync()).ViewId = hasView ? viewId : null;
            await db.SaveChangesAsync();
        }

        var viewReads = 0;
        var workspaceCreates = 0;
        var viewDeleted = false;
        var workspaceDeleted = false;
        string variables = null;
        env.Http.Handle = async request =>
        {
            var path = request.RequestUri.AbsolutePath.ToLowerInvariant();
            if (request.RequestUri.Host == "player.test")
            {
                Assert.True(hasView);
                if (request.Method == HttpMethod.Delete)
                {
                    Assert.EndsWith($"/views/{viewId}", path);
                    viewDeleted = true;
                    return new(HttpStatusCode.NoContent);
                }
                if (path.EndsWith("/teams"))
                    return FakeHttp.Json(Array.Empty<object>());
                Assert.EndsWith($"/views/{viewId}", path);
                if (++viewReads == 1)
                    return FakeHttp.Json(new { message = "Configuration unavailable" }, firstReadStatus);
                return FakeHttp.Json(new { id = viewId });
            }
            Assert.Equal("caster.test", request.RequestUri.Host);
            if (request.Method == HttpMethod.Post && path.EndsWith("/workspaces"))
            {
                workspaceCreates++;
                return FakeHttp.Json(new { id = workspaceId }, HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/files"))
            {
                using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync());
                variables = body.RootElement.GetProperty("content").GetString();
                // End after workspace preparation to isolate this state from launch polling.
                await env.RequestEnd(entity.Id);
                return FakeHttp.Json(new { id = Guid.NewGuid() }, HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Get && (path.EndsWith("/runs") || path.EndsWith("/resources")))
                return FakeHttp.Json(Array.Empty<object>());
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.EndsWith($"/workspaces/{workspaceId}", path);
            workspaceDeleted = true;
            return new(HttpStatusCode.NoContent);
        };

        await Process(env, entity);
        var saved = await env.Read(entity.Id);
        Assert.Equal(hasView, viewDeleted);
        Assert.Null(saved.ViewId);
        Assert.Null(saved.WorkspaceId);
        Assert.NotNull(saved.EndDate);
        if (firstReadStatus == HttpStatusCode.NotFound)
        {
            Assert.Equal(1, viewReads);
            Assert.Equal(0, workspaceCreates);
            Assert.False(workspaceDeleted);
            Assert.Null(variables);
            Assert.Equal(EventStatus.Failed, saved.Status);
            Assert.Equal(InternalEventStatus.FailedLaunch, saved.InternalStatus);
            Assert.Equal(InternalEventStatus.CreatingWorkspace, saved.LastLaunchInternalStatus);
            Assert.Contains("read the virtual environment configuration", saved.ErrorMessage);
        }
        else
        {
            Assert.Equal(hasView ? 2 : 0, viewReads);
            Assert.Equal(1, workspaceCreates);
            Assert.True(workspaceDeleted);
            if (hasView)
                Assert.Contains($"view_id = \"{viewId}\"", variables);
            else
                Assert.Equal(string.Empty, variables);
            Assert.Equal(EventStatus.Ended, saved.Status);
            Assert.Equal(default, saved.LastLaunchInternalStatus);
            Assert.Null(saved.ErrorMessage);
        }
    }

    [Theory]
    [InlineData(EventStatus.Creating, InternalEventStatus.LaunchQueued)]
    [InlineData(EventStatus.Planning, InternalEventStatus.PlanningLaunch)]
    [InlineData(EventStatus.Applying, InternalEventStatus.StartingScenario)]
    [InlineData(EventStatus.Active, InternalEventStatus.Launched)]
    [InlineData(EventStatus.Paused, InternalEventStatus.Launched)]
    public async Task PersistedIntentIsRecoveredBeforeAnyMoreLaunchWork(EventStatus status, InternalEventStatus step)
    {
        using var env = new TestEnvironment(postgres);
        var entity = await env.Seed(status, step);
        // Simulate a request committed immediately before a process restart/lost enqueue.
        using (var request = env.Context())
        {
            var row = await request.Events.SingleAsync();
            await EventLifecycle.RequestEndAsync(request, row, DateTime.UtcNow, default);
        }
        using (var recovery = env.Context())
            Assert.Single(await EventLifecycle.UnfinishedEvents(recovery).ToListAsync());
        await Process(env, entity);
        var saved = await env.Read(entity.Id);
        Assert.Equal(EventStatus.Ended, saved.Status);
        Assert.NotNull(saved.EndDate);
        Assert.True(saved.EndDate >= saved.EndRequestedAt);
        Assert.Null(saved.LaunchDate);
        Assert.Null(saved.ErrorMessage);
        Assert.Empty(env.Http.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EndDuringCasterPollingCancelsBeforeWorkspaceDeletion(bool applying)
    {
        using var env = new TestEnvironment(postgres);
        var entity = await env.Seed(applying ? EventStatus.Applying : EventStatus.Planning,
            applying ? InternalEventStatus.AppliedLaunch : InternalEventStatus.PlannedLaunch);
        var run = new Run { Id = Guid.NewGuid(), Status = applying ? RunStatus.Applying : RunStatus.Planning };
        using (var db = env.Context())
        {
            var row = await db.Events.SingleAsync();
            row.WorkspaceId = Guid.NewGuid();
            row.RunId = run.Id;
            await db.SaveChangesAsync();
        }
        var endSent = false;
        var cancelled = false;
        env.Http.Handle = async request =>
        {
            var path = request.RequestUri.AbsolutePath.ToLowerInvariant();
            if (path.EndsWith("/actions/cancel"))
            {
                cancelled = true;
                run.Status = RunStatus.Rejected;
                return FakeHttp.Json(run);
            }
            if (request.Method == HttpMethod.Delete)
            {
                Assert.True(cancelled);
                Assert.Equal(RunStatus.Rejected, run.Status);
                return new(HttpStatusCode.NoContent);
            }
            if (path.EndsWith("/resources"))
            {
                Assert.True(cancelled);
                return FakeHttp.Json(Array.Empty<object>());
            }
            if (path.EndsWith("/runs"))
                return FakeHttp.Json(new[] { run });
            Assert.Contains($"/runs/{run.Id}", path);
            if (!endSent)
            {
                endSent = true;
                await env.RequestEnd(entity.Id);
            }
            return FakeHttp.Json(run);
        };
        await Process(env, entity);
        var saved = await env.Read(entity.Id);
        Assert.True(cancelled, saved.ErrorDetail);
        Assert.Equal(EventStatus.Ended, saved.Status);
        Assert.Null(saved.WorkspaceId);
        Assert.Null(saved.RunId);
        Assert.Null(saved.ErrorMessage);
        Assert.Equal(default, saved.LastLaunchInternalStatus);
        Assert.DoesNotContain(env.Http.Requests, x => x.EndsWith("apply"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EndDuringScenarioCallPersistsItsIdBeforeCleanup(bool starting)
    {
        using var env = new TestEnvironment(postgres);
        var entity = await env.Seed(starting ? EventStatus.Applying : EventStatus.Creating,
            starting ? InternalEventStatus.StartingScenario : InternalEventStatus.CreatingScenario);
        var scenarioId = Guid.NewGuid();
        using (var db = env.Context())
        {
            var row = await db.Events.SingleAsync();
            var template = await db.EventTemplates.SingleAsync();
            template.ScenarioTemplateId = Guid.NewGuid();
            if (starting) row.ScenarioId = scenarioId;
            await db.SaveChangesAsync();
        }
        var scenarioEnded = false;
        env.Http.Handle = async request =>
        {
            var path = request.RequestUri.AbsolutePath.ToLowerInvariant();
            if (path.EndsWith("/end"))
            {
                Assert.Equal(scenarioId, (await env.Read(entity.Id)).ScenarioId);
                scenarioEnded = true;
            }
            else
            {
                Assert.True(starting ? path.EndsWith("/start") : path.Contains("/scenariotemplates/"));
                await env.RequestEnd(entity.Id);
            }
            return FakeHttp.Json(new { id = scenarioId },
                !starting && !path.EndsWith("/end") ? HttpStatusCode.Created : HttpStatusCode.OK);
        };
        await Process(env, entity);
        var saved = await env.Read(entity.Id);
        Assert.True(scenarioEnded, saved.ErrorDetail);
        Assert.Equal(EventStatus.Ended, saved.Status);
        Assert.Null(saved.ScenarioId);
        Assert.NotNull(saved.EndDate);
        Assert.Null(saved.ErrorMessage);
        if (!starting)
            Assert.DoesNotContain(env.Http.Requests, x => x.EndsWith("/start"));
    }

    [Fact]
    public async Task CancellationTimeoutLeavesResourcesAndReportsCleanupFailure()
    {
        using var env = new TestEnvironment(postgres);
        env.Options.CasterDestroyMaxWaitMinutes = 0;
        var entity = await env.Seed(EventStatus.Applying, InternalEventStatus.AppliedLaunch);
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Applying };
        using (var db = env.Context())
        {
            var row = await db.Events.SingleAsync();
            row.WorkspaceId = Guid.NewGuid();
            row.RunId = run.Id;
            await db.SaveChangesAsync();
        }
        await env.RequestEnd(entity.Id);
        env.Http.Handle = request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(FakeHttp.Json(new[] { run }));
        };
        await Process(env, entity);
        var saved = await env.Read(entity.Id);
        Assert.Equal(EventStatus.Failed, saved.Status);
        Assert.Equal(InternalEventStatus.FailedDestroy, saved.InternalStatus);
        Assert.NotNull(saved.WorkspaceId);
        Assert.Equal(run.Id, saved.RunId);
        Assert.Null(saved.EndDate);
        Assert.True(saved.ErrorMessage.Contains("did not stop"), saved.ErrorDetail);
        Assert.Equal(default, saved.LastLaunchInternalStatus);
    }

    [Fact]
    public async Task CancellationDeadlineAlsoBoundsAuthenticationFailures()
    {
        using var env = new TestEnvironment(postgres);
        env.Options.CasterDestroyMaxWaitMinutes = 0;
        env.Options.ApiClientEndFailureMaxRetries = 0;
        env.Services.GetRequiredService<ResourceOwnerAuthorizationOptions>().Authority = "https://unreachable.test";
        env.Http.Handle = _ => throw new HttpRequestException("identity unavailable");
        var entity = await env.Seed(EventStatus.Active);
        using (var db = env.Context())
        {
            var row = await db.Events.SingleAsync();
            row.WorkspaceId = Guid.NewGuid();
            await db.SaveChangesAsync();
        }
        await env.RequestEnd(entity.Id);
        await Process(env, entity);
        var saved = await env.Read(entity.Id);
        Assert.Equal(EventStatus.Failed, saved.Status);
        Assert.NotNull(saved.WorkspaceId);
        Assert.Null(saved.EndDate);
        Assert.Contains("could not be confirmed stopped", saved.ErrorMessage);
    }

    [Fact]
    public async Task OldCleanupStateReconcilesAnUnrejectedLaunchPlan()
    {
        using var env = new TestEnvironment(postgres);
        var entity = await env.Seed(EventStatus.Ending, InternalEventStatus.DeletingWorkspace);
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Planned };
        using (var db = env.Context())
        {
            var row = await db.Events.SingleAsync();
            row.WorkspaceId = Guid.NewGuid();
            row.RunId = run.Id;
            await db.SaveChangesAsync();
        }
        var rejected = false;
        env.Http.Handle = request =>
        {
            var path = request.RequestUri.AbsolutePath;
            if (path.EndsWith("/actions/reject"))
            {
                rejected = true;
                run.Status = RunStatus.Rejected;
                return Task.FromResult(FakeHttp.Json(run));
            }
            if (path.EndsWith("/runs"))
                return Task.FromResult(FakeHttp.Json(new[] { run }));
            Assert.True(rejected);
            if (path.EndsWith("/resources"))
                return Task.FromResult(FakeHttp.Json(Array.Empty<object>()));
            Assert.Equal(HttpMethod.Delete, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        };
        await Process(env, entity);
        var saved = await env.Read(entity.Id);
        Assert.True(rejected);
        Assert.Equal(EventStatus.Ended, saved.Status);
        Assert.Null(saved.WorkspaceId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveringDestroyKeepsItsRunAcrossTransientPollErrors(bool applying)
    {
        using var env = new TestEnvironment(postgres);
        var entity = await env.Seed(EventStatus.Ending,
            applying ? InternalEventStatus.AppliedDestroy : InternalEventStatus.PlannedDestroy);
        var run = new Run
        {
            Id = Guid.NewGuid(), IsDestroy = true,
            Status = applying ? RunStatus.Applying : RunStatus.Planning
        };
        using (var db = env.Context())
        {
            var row = await db.Events.SingleAsync();
            row.WorkspaceId = Guid.NewGuid();
            row.RunId = run.Id;
            row.EndRequestedAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        var reads = 0;
        env.Http.Handle = async request =>
        {
            var path = request.RequestUri.AbsolutePath;
            Assert.DoesNotContain("cancel", path);
            Assert.DoesNotContain("reject", path);
            if (path.EndsWith("/runs"))
                return FakeHttp.Json(new[] { run });
            if (path.EndsWith("/resources"))
            {
                Assert.Equal(RunStatus.Applied, run.Status);
                return FakeHttp.Json(Array.Empty<object>());
            }
            if (request.Method == HttpMethod.Delete)
                return new(HttpStatusCode.NoContent);
            if (path.EndsWith("/actions/apply"))
            {
                run.Status = RunStatus.Applying;
                run.ApplyId = Guid.NewGuid();
                return FakeHttp.Json(new Apply { Id = run.ApplyId.Value });
            }
            Assert.Contains($"/runs/{run.Id}", path);
            if (++reads == 1)
                throw new HttpRequestException("poll response lost");
            Assert.Equal(run.Id, (await env.Read(entity.Id)).RunId);
            run.Status = run.Status == RunStatus.Planning ? RunStatus.Planned
                : run.Status == RunStatus.Applying ? RunStatus.Applied : run.Status;
            return FakeHttp.Json(run);
        };
        await Process(env, entity);
        var saved = await env.Read(entity.Id);
        Assert.Equal(EventStatus.Ended, saved.Status);
        Assert.Null(saved.RunId);
        Assert.Null(saved.ErrorMessage);
        Assert.True(reads >= 2);
    }

    [Fact]
    public async Task ExplicitRetryOfFailedCleanupPreservesTheLaunchFailure()
    {
        using var env = new TestEnvironment(postgres);
        var entity = await env.Seed(EventStatus.Failed, InternalEventStatus.FailedDestroy);
        using (var db = env.Context())
        {
            var row = await db.Events.SingleAsync();
            row.LastLaunchInternalStatus = InternalEventStatus.CreatingWorkspace;
            row.ErrorMessage = "The original launch failed.";
            row.EndRequestedAt = DateTime.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();
        }
        await env.RequestEnd(entity.Id);
        await Process(env, entity);
        var saved = await env.Read(entity.Id);
        Assert.Equal(EventStatus.Failed, saved.Status);
        Assert.Equal(InternalEventStatus.FailedLaunch, saved.InternalStatus);
        Assert.Equal("The original launch failed.", saved.ErrorMessage);
        Assert.NotNull(saved.EndDate);
    }

    [Fact]
    public void QueueRetainsANotificationReceivedWhileProcessing()
    {
        var queue = new AlloyEventQueue();
        var entity = new EventEntity { Id = Guid.NewGuid() };
        queue.Add(entity);
        queue.Take(default);
        queue.Add(entity);
        queue.Complete(entity);
        Assert.Equal(entity.Id, queue.Take(default).Id);
        queue.Complete(entity);
    }

    [Fact]
    public void RecoveryDoesNotQueueAnExtraAttemptBehindTheWorker()
    {
        var queue = new AlloyEventQueue();
        var entity = new EventEntity { Id = Guid.NewGuid() };
        queue.Add(entity);
        queue.Take(default);
        queue.Add(entity, requeueIfProcessing: false);
        queue.Complete(entity);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => queue.Take(cancelled.Token));
    }

    [Fact]
    public async Task StaleRecoveryNotificationCannotRestartFailedCleanup()
    {
        using var env = new TestEnvironment(postgres);
        var notification = await env.Seed(EventStatus.Ending, InternalEventStatus.EndQueued);
        using (var db = env.Context())
        {
            var entity = await db.Events.SingleAsync();
            entity.Status = EventStatus.Failed;
            entity.InternalStatus = InternalEventStatus.FailedDestroy;
            entity.EndRequestedAt = DateTime.UtcNow;
            entity.FailureCount = 2;
            await db.SaveChangesAsync();
        }
        await Process(env, notification);
        var saved = await env.Read(notification.Id);
        Assert.Equal(EventStatus.Failed, saved.Status);
        Assert.Equal(2, saved.FailureCount);
        Assert.Null(saved.EndDate);
    }
}
