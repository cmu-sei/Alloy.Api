using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Alloy.Api.Data.Models;
using Alloy.Api.Infrastructure.Extensions;
using Caster.Api.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Alloy.Api.Tests;

public class CasterCancellationTests
{
    private static CasterApiClient Client(FakeHttp http)
    {
        var client = http.CreateClient();
        client.BaseAddress = new Uri("https://caster.test");
        return new CasterApiClient(client);
    }

    [Theory]
    [InlineData(RunStatus.Queued)]
    [InlineData(RunStatus.Planning)]
    [InlineData(RunStatus.ApplyQueued)]
    [InlineData(RunStatus.Applying)]
    public async Task GracefulCancellationWaitsForTheRunToSettle(RunStatus status)
    {
        using var http = new FakeHttp();
        var run = new Run { Id = Guid.NewGuid(), Status = status };
        var cancelled = false;
        var reads = 0;
        http.Handle = async request =>
        {
            if (request.RequestUri.AbsolutePath.EndsWith("/actions/cancel"))
            {
                Assert.False(cancelled);
                Assert.Contains("\"force\":false", (await request.Content.ReadAsStringAsync()).ToLowerInvariant());
                cancelled = true;
                return FakeHttp.Json(run); // cancellation acknowledgement is NOT terminal
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            if (++reads >= 3 && cancelled) run.Status = RunStatus.Rejected;
            return FakeHttp.Json(new[] { run });
        };
        var result = await CasterApiExtensions.SettleLaunchRunsAsync(
            new EventEntity { WorkspaceId = Guid.NewGuid() }, Client(http), 0,
            DateTime.UtcNow.AddSeconds(5), NullLogger.Instance, default);
        Assert.True(result.IsSuccess);
        Assert.True(cancelled);
        Assert.True(reads >= 3);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task PlannedRunIsRejectedAndCompletionRaceIsReconciled()
    {
        using var http = new FakeHttp();
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Planned };
        var rejected = false;
        http.Handle = request =>
        {
            if (request.RequestUri.AbsolutePath.EndsWith("/actions/reject"))
            {
                rejected = true;
                run.Status = RunStatus.Applied;
                return Task.FromResult(FakeHttp.Json(new { error = "already applied" }, HttpStatusCode.Conflict));
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(FakeHttp.Json(new[] { run }));
        };
        var result = await CasterApiExtensions.SettleLaunchRunsAsync(
            new EventEntity { WorkspaceId = Guid.NewGuid(), RunId = null }, Client(http), 0,
            DateTime.UtcNow.AddSeconds(5), NullLogger.Instance, default);
        Assert.True(result.IsSuccess);
        Assert.True(rejected);
        Assert.DoesNotContain(http.Requests, x => x.Contains("cancel"));
    }

    [Theory]
    [InlineData(RunStatus.Applied__State_Error)]
    [InlineData(RunStatus.Failed__State_Error)]
    public async Task StateErrorsAreSavedBeforeCleanup(RunStatus status)
    {
        using var http = new FakeHttp();
        var run = new Run { Id = Guid.NewGuid(), Status = status };
        http.Handle = request =>
        {
            if (request.RequestUri.AbsolutePath.EndsWith("/actions/save-state"))
            {
                run.Status = RunStatus.Failed;
                return Task.FromResult(FakeHttp.Json(run));
            }
            return Task.FromResult(FakeHttp.Json(new[] { run }));
        };
        var result = await CasterApiExtensions.SettleLaunchRunsAsync(
            new EventEntity { WorkspaceId = Guid.NewGuid() }, Client(http), 0,
            DateTime.UtcNow.AddSeconds(5), NullLogger.Instance, default);
        Assert.True(result.IsSuccess);
        Assert.Contains(http.Requests, x => x.EndsWith("save-state"));
    }

    [Fact]
    public async Task ExistingDestroyIsReturnedForResumeWithoutCancellation()
    {
        using var http = new FakeHttp();
        var run = new Run { Id = Guid.NewGuid(), IsDestroy = true, Status = RunStatus.Applying };
        http.Handle = request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(FakeHttp.Json(new[] { run }));
        };
        var result = await CasterApiExtensions.SettleLaunchRunsAsync(
            new EventEntity { WorkspaceId = Guid.NewGuid() }, Client(http), 0,
            DateTime.UtcNow.AddSeconds(5), NullLogger.Instance, default);
        Assert.True(result.IsSuccess);
        Assert.Equal(run.Id, result.Value.Id);
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task LostCancellationResponseIsReconciledOnRetry()
    {
        using var http = new FakeHttp();
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Applying };
        http.Handle = request =>
        {
            if (request.RequestUri.AbsolutePath.EndsWith("cancel"))
            {
                run.Status = RunStatus.Rejected;
                throw new HttpRequestException("response lost");
            }
            return Task.FromResult(FakeHttp.Json(new[] { run }));
        };
        var entity = new EventEntity { WorkspaceId = Guid.NewGuid(), RunId = run.Id };
        var client = Client(http);
        var first = await CasterApiExtensions.SettleLaunchRunsAsync(entity, client, 0,
            DateTime.UtcNow.AddSeconds(5), NullLogger.Instance, default);
        Assert.True(first.IsTransient);
        var retry = await CasterApiExtensions.SettleLaunchRunsAsync(entity, client, 0,
            DateTime.UtcNow.AddSeconds(5), NullLogger.Instance, default);
        Assert.True(retry.IsSuccess, retry.Detail);
        Assert.Equal(run.Id, entity.RunId);
    }

    [Fact]
    public async Task SettlementTimeoutDoesNotClearIdsOrForceCancellation()
    {
        using var http = new FakeHttp();
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Applying };
        http.Handle = request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(FakeHttp.Json(new[] { run }));
        };
        var entity = new EventEntity { WorkspaceId = Guid.NewGuid(), RunId = run.Id };
        var result = await CasterApiExtensions.SettleLaunchRunsAsync(entity, Client(http), 0,
            DateTime.UtcNow.AddSeconds(-1), NullLogger.Instance, default);
        Assert.True(result.IsPermanent);
        Assert.Equal(run.Id, entity.RunId);
        Assert.NotNull(entity.WorkspaceId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaunchPollingYieldsToEndIntent(bool applying)
    {
        using var http = new FakeHttp();
        var reads = 0;
        var run = new Run { Id = Guid.NewGuid(), Status = applying ? RunStatus.Applying : RunStatus.Planning };
        http.Handle = _ =>
        {
            reads++;
            return Task.FromResult(FakeHttp.Json(run));
        };
        var entity = new EventEntity { RunId = run.Id };
        Task<bool> EndRequested() => Task.FromResult(reads > 0);
        var result = applying
            ? await CasterApiExtensions.WaitForRunToBeAppliedAsync(entity, Client(http), 0, 1, false, NullLogger.Instance, default, EndRequested)
            : await CasterApiExtensions.WaitForRunToBePlannedAsync(entity, Client(http), 0, 1, false, NullLogger.Instance, default, EndRequested);
        Assert.True(result.IsEndRequested);
        Assert.False(result.IsPermanent);
        Assert.False(result.IsTransient);
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task DestroyPollingIgnoresLaunchEndIntent()
    {
        using var http = new FakeHttp();
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Applied };
        http.Handle = _ => Task.FromResult(FakeHttp.Json(run));
        var result = await CasterApiExtensions.WaitForRunToBeAppliedAsync(
            new EventEntity { RunId = run.Id }, Client(http), 0, 1, true,
            NullLogger.Instance, default, () => throw new InvalidOperationException("must not check end intent"));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AcceptedApplyWithLostResponseIsNotSubmittedAgain()
    {
        using var http = new FakeHttp();
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Planned };
        var applies = 0;
        http.Handle = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                applies++;
                run.Status = RunStatus.ApplyQueued;
                run.ApplyId = Guid.NewGuid();
                throw new HttpRequestException("accepted but response lost");
            }
            return Task.FromResult(FakeHttp.Json(run));
        };
        var entity = new EventEntity { RunId = run.Id };
        var client = Client(http);
        Assert.True((await CasterApiExtensions.ApplyRunAsync(entity, client, NullLogger.Instance, default)).IsTransient);
        Assert.True((await CasterApiExtensions.ApplyRunAsync(entity, client, NullLogger.Instance, default)).IsSuccess);
        Assert.Equal(1, applies);
    }

    [Fact]
    public async Task AcceptedCreateWithLostResponseIsRecoveredFromWorkspace()
    {
        using var http = new FakeHttp();
        var run = new Run { Id = Guid.NewGuid(), Status = RunStatus.Planning };
        var creates = 0;
        http.Handle = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                creates++;
                throw new HttpRequestException("accepted but response lost");
            }
            return Task.FromResult(FakeHttp.Json(creates == 0 ? Array.Empty<Run>() : new[] { run }));
        };
        var entity = new EventEntity { WorkspaceId = Guid.NewGuid() };
        var client = Client(http);
        Assert.True((await CasterApiExtensions.CreateRunAsync(entity, client, false, NullLogger.Instance, default)).IsTransient);
        var recovered = await CasterApiExtensions.CreateRunAsync(entity, client, false, NullLogger.Instance, default);
        Assert.True(recovered.IsSuccess);
        Assert.Equal(run.Id, recovered.Value);
        Assert.Equal(1, creates);
    }
}
