// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data.Models;
using Alloy.Api.Infrastructure.Extensions;
using Alloy.Api.Tests.Support;
using Caster.Api.Client;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alloy.Api.Tests.Infrastructure.Extensions;

/// <summary>
/// The Caster run helpers the worker drives, over the real generated client and a scripted Caster: a
/// lost response or a lock conflict is retried against the same run, cancellation waits for the run to
/// settle, and a destroy is never cancelled.
/// </summary>
public class CasterApiExtensionsTests
{
    private const string Caster = "https://caster.test";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A save-state conflict is transient; the retry polls and saves the same run.</summary>
    [Theory]
    [InlineData(false, RunStatus.Applied__State_Error, RunStatus.Applied)]
    [InlineData(true, RunStatus.Applied__State_Error, RunStatus.Applied)]
    [InlineData(false, RunStatus.Failed__State_Error, RunStatus.Failed)]
    [InlineData(true, RunStatus.Failed__State_Error, RunStatus.Failed)]
    public async Task WaitForRunToBeApplied_retries_a_state_save_lock_conflict_on_the_same_run(bool isDestroy, RunStatus initial, RunStatus terminal)
    {
        using var caster = new FakeSiblingApis();
        var run = new Run { Id = Guid.NewGuid(), Status = initial, IsDestroy = isDestroy };
        var saves = 0;
        caster.Handle = request =>
        {
            if (request.RequestUri.AbsolutePath.EndsWith($"/runs/{run.Id}/actions/save-state", StringComparison.Ordinal) && ++saves == 1)
            {
                return Task.FromResult(FakeSiblingApis.Json(new { error = "workspace locked" }, HttpStatusCode.Conflict));
            }

            run.Status = saves > 1 ? terminal : run.Status;
            return Task.FromResult(FakeSiblingApis.Json(run));
        };
        var entity = new EventEntity { RunId = run.Id };
        var client = Client(caster);
        var first = await CasterApiExtensions.WaitForRunToBeAppliedAsync(entity, client, 0, 1, isDestroy, NullLogger.Instance, Ct);
        var runIdAfterFirst = entity.RunId;

        var second = await CasterApiExtensions.WaitForRunToBeAppliedAsync(entity, client, 0, 1, isDestroy, NullLogger.Instance, Ct);

        Assert.True(first.IsTransient);
        Assert.Equal(run.Id, runIdAfterFirst);
        Assert.Equal((terminal == RunStatus.Applied, terminal == RunStatus.Failed), (second.IsSuccess, second.IsPermanent));
        Assert.Equal(2, saves);
        Assert.Equal(run.Id, entity.RunId);
        Assert.All(caster.Requests, x => Assert.Matches($"^(GET /api/runs/{run.Id}|POST /api/runs/{run.Id}/actions/save-state)$", x));
    }

    /// <summary>A graceful cancel is acknowledged before the run stops; settling waits for a terminal status.</summary>
    [Theory]
    [InlineData(RunStatus.Queued)]
    [InlineData(RunStatus.Planning)]
    [InlineData(RunStatus.ApplyQueued)]
    [InlineData(RunStatus.Applying)]
    public async Task SettleLaunchRuns_waits_for_a_gracefully_cancelled_run_to_settle(RunStatus status)
    {
        using var caster = new FakeSiblingApis();
        var run = new Run { Id = Guid.NewGuid(), Status = status };
        string cancelBody = null;
        var reads = 0;
        caster.Handle = async request =>
        {
            if (request.RequestUri.AbsolutePath.EndsWith("/actions/cancel", StringComparison.Ordinal))
            {
                cancelBody = await request.Content.ReadAsStringAsync(Ct);
                return FakeSiblingApis.Json(run);
            }

            run.Status = ++reads >= 3 && cancelBody is not null ? RunStatus.Rejected : run.Status;
            return FakeSiblingApis.Json(new[] { run });
        };

        var entity = new EventEntity { WorkspaceId = Guid.NewGuid() };

        var result = await CasterApiExtensions.SettleLaunchRunsAsync(
            entity, Client(caster), 0, DateTime.UtcNow.AddSeconds(5), NullLogger.Instance, Ct);

        Assert.True(result.IsSuccess);
        Assert.Contains("\"force\":false", cancelBody.ToLowerInvariant());
        Assert.Single(caster.Requests, x => x.EndsWith("/actions/cancel", StringComparison.Ordinal));
        Assert.All(caster.Requests, x => Assert.Matches($"^(GET /api/workspaces/{entity.WorkspaceId}/runs|POST /api/runs/{run.Id}/actions/cancel)$", x));
        Assert.True(reads >= 3);
        Assert.Null(result.Value);
    }

    /// <summary>A planned run is rejected rather than cancelled; a conflict because it was applied meanwhile is reconciled.</summary>
    [Fact]
    public async Task SettleLaunchRuns_rejects_a_planned_run_and_reconciles_a_completion_race()
    {
        using var caster = new FakeSiblingApis();
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Planned };
        caster.Handle = request =>
        {
            if (request.RequestUri.AbsolutePath.EndsWith("/actions/reject", StringComparison.Ordinal))
            {
                run.Status = RunStatus.Applied;
                return Task.FromResult(FakeSiblingApis.Json(new { error = "already applied" }, HttpStatusCode.Conflict));
            }

            return Task.FromResult(FakeSiblingApis.Json(new[] { run }));
        };

        var entity = new EventEntity { WorkspaceId = Guid.NewGuid(), RunId = null };

        var result = await CasterApiExtensions.SettleLaunchRunsAsync(
            entity, Client(caster), 0, DateTime.UtcNow.AddSeconds(5), NullLogger.Instance, Ct);

        Assert.True(result.IsSuccess);
        Assert.Contains(caster.Requests, x => x.EndsWith("/actions/reject", StringComparison.Ordinal));
        Assert.DoesNotContain(caster.Requests, x => x.Contains("cancel", StringComparison.Ordinal));
        Assert.All(caster.Requests, x => Assert.Matches($"^(GET /api/workspaces/{entity.WorkspaceId}/runs|POST /api/runs/{run.Id}/actions/reject)$", x));
    }

    [Theory]
    [InlineData(RunStatus.Applied__State_Error)]
    [InlineData(RunStatus.Failed__State_Error)]
    public async Task SettleLaunchRuns_saves_an_errored_state_before_cleanup(RunStatus status)
    {
        using var caster = new FakeSiblingApis();
        var run = new Run { Id = Guid.NewGuid(), Status = status };
        caster.Handle = request =>
        {
            if (request.RequestUri.AbsolutePath.EndsWith("/actions/save-state", StringComparison.Ordinal))
            {
                run.Status = RunStatus.Failed;
                return Task.FromResult(FakeSiblingApis.Json(run));
            }

            return Task.FromResult(FakeSiblingApis.Json(new[] { run }));
        };

        var result = await CasterApiExtensions.SettleLaunchRunsAsync(
            new EventEntity { WorkspaceId = Guid.NewGuid() }, Client(caster), 0,
            DateTime.UtcNow.AddSeconds(5), NullLogger.Instance, Ct);

        Assert.True(result.IsSuccess);
        Assert.Contains(caster.Requests, x => x.EndsWith("save-state", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SettleLaunchRuns_returns_a_destroy_in_progress_for_resuming_without_cancelling_it()
    {
        using var caster = new FakeSiblingApis();
        var run = new Run { Id = Guid.NewGuid(), IsDestroy = true, Status = RunStatus.Applying };
        caster.Handle = _ => Task.FromResult(FakeSiblingApis.Json(new[] { run }));

        var result = await CasterApiExtensions.SettleLaunchRunsAsync(
            new EventEntity { WorkspaceId = Guid.NewGuid() }, Client(caster), 0,
            DateTime.UtcNow.AddSeconds(5), NullLogger.Instance, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(run.Id, result.Value.Id);
        Assert.StartsWith("GET ", Assert.Single(caster.Requests));
    }

    /// <summary>The cancel was accepted but its response lost; the retry finds the run rejected and keeps its id.</summary>
    [Fact]
    public async Task SettleLaunchRuns_reconciles_a_lost_cancellation_response_on_retry()
    {
        using var caster = new FakeSiblingApis();
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Applying };
        caster.Handle = request =>
        {
            if (request.RequestUri.AbsolutePath.EndsWith("cancel", StringComparison.Ordinal))
            {
                run.Status = RunStatus.Rejected;
                throw new HttpRequestException("response lost");
            }

            return Task.FromResult(FakeSiblingApis.Json(new[] { run }));
        };
        var entity = new EventEntity { WorkspaceId = Guid.NewGuid(), RunId = run.Id };
        var client = Client(caster);
        var first = await CasterApiExtensions.SettleLaunchRunsAsync(entity, client, 0,
            DateTime.UtcNow.AddSeconds(5), NullLogger.Instance, Ct);

        var retry = await CasterApiExtensions.SettleLaunchRunsAsync(entity, client, 0,
            DateTime.UtcNow.AddSeconds(5), NullLogger.Instance, Ct);

        Assert.True(first.IsTransient);
        Assert.True(retry.IsSuccess, retry.Detail);
        Assert.Equal(run.Id, entity.RunId);
    }

    [Fact]
    public async Task SettleLaunchRuns_past_its_deadline_keeps_the_ids_and_never_forces_a_cancel()
    {
        using var caster = new FakeSiblingApis();
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Applying };
        caster.Handle = _ => Task.FromResult(FakeSiblingApis.Json(new[] { run }));
        var entity = new EventEntity { WorkspaceId = Guid.NewGuid(), RunId = run.Id };

        var result = await CasterApiExtensions.SettleLaunchRunsAsync(entity, Client(caster), 0,
            DateTime.UtcNow.AddSeconds(-1), NullLogger.Instance, Ct);

        Assert.True(result.IsPermanent);
        Assert.Equal(run.Id, entity.RunId);
        Assert.NotNull(entity.WorkspaceId);
        Assert.All(caster.Requests, x => Assert.StartsWith("GET ", x));
    }

    [Fact]
    public async Task WaitForRunToBePlanned_yields_to_an_end_request()
    {
        using var caster = new FakeSiblingApis();
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Planning };
        caster.Handle = _ => Task.FromResult(FakeSiblingApis.Json(run));

        var result = await CasterApiExtensions.WaitForRunToBePlannedAsync(
            new EventEntity { RunId = run.Id }, Client(caster), 0, 1, false, NullLogger.Instance, Ct,
            () => Task.FromResult(caster.Requests.Count > 0));

        Assert.Equal((true, false, false), (result.IsEndRequested, result.IsPermanent, result.IsTransient));
        Assert.Single(caster.Requests);
    }

    [Fact]
    public async Task WaitForRunToBeApplied_yields_to_an_end_request()
    {
        using var caster = new FakeSiblingApis();
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Applying };
        caster.Handle = _ => Task.FromResult(FakeSiblingApis.Json(run));

        var result = await CasterApiExtensions.WaitForRunToBeAppliedAsync(
            new EventEntity { RunId = run.Id }, Client(caster), 0, 1, false, NullLogger.Instance, Ct,
            () => Task.FromResult(caster.Requests.Count > 0));

        Assert.Equal((true, false, false), (result.IsEndRequested, result.IsPermanent, result.IsTransient));
        Assert.Single(caster.Requests);
    }

    /// <summary>The end-intent check would throw; a destroy never asks it.</summary>
    [Fact]
    public async Task WaitForRunToBeApplied_for_a_destroy_ignores_end_intent()
    {
        using var caster = new FakeSiblingApis();
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Applied };
        caster.Handle = _ => Task.FromResult(FakeSiblingApis.Json(run));

        var result = await CasterApiExtensions.WaitForRunToBeAppliedAsync(
            new EventEntity { RunId = run.Id }, Client(caster), 0, 1, true, NullLogger.Instance, Ct,
            () => throw new InvalidOperationException("must not check end intent"));

        Assert.True(result.IsSuccess);
    }

    /// <summary>An apply accepted with its response lost is found queued on the retry and not posted again.</summary>
    [Fact]
    public async Task ApplyRun_does_not_submit_again_an_apply_whose_response_was_lost()
    {
        using var caster = new FakeSiblingApis();
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Planned };
        caster.Handle = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                run.Status = RunStatus.ApplyQueued;
                run.ApplyId = Guid.NewGuid();
                throw new HttpRequestException("accepted but response lost");
            }

            return Task.FromResult(FakeSiblingApis.Json(run));
        };
        var entity = new EventEntity { RunId = run.Id };
        var client = Client(caster);
        var first = await CasterApiExtensions.ApplyRunAsync(entity, client, NullLogger.Instance, Ct);

        var retry = await CasterApiExtensions.ApplyRunAsync(entity, client, NullLogger.Instance, Ct);

        Assert.True(first.IsTransient);
        Assert.True(retry.IsSuccess);
        Assert.Single(caster.Requests, x => x.StartsWith("POST ", StringComparison.Ordinal));
    }

    /// <summary>A run created with its response lost is recovered from the workspace's runs, not created twice.</summary>
    [Fact]
    public async Task CreateRun_recovers_a_run_whose_create_response_was_lost()
    {
        using var caster = new FakeSiblingApis();
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Planning };
        var created = false;
        caster.Handle = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                created = true;
                throw new HttpRequestException("accepted but response lost");
            }

            return Task.FromResult(FakeSiblingApis.Json(created ? new[] { run } : Array.Empty<Run>()));
        };
        var entity = new EventEntity { WorkspaceId = Guid.NewGuid() };
        var client = Client(caster);
        var first = await CasterApiExtensions.CreateRunAsync(entity, client, false, NullLogger.Instance, Ct);

        var recovered = await CasterApiExtensions.CreateRunAsync(entity, client, false, NullLogger.Instance, Ct);

        Assert.True(first.IsTransient);
        Assert.True(recovered.IsSuccess);
        Assert.Equal(run.Id, recovered.Value);
        Assert.Single(caster.Requests, x => x.StartsWith("POST ", StringComparison.Ordinal));
    }

    private static CasterApiClient Client(FakeSiblingApis caster)
    {
        var client = caster.CreateClient();
        client.BaseAddress = new Uri(Caster);

        return new CasterApiClient(client);
    }
}
