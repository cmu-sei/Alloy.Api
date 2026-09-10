using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alloy.Api.Tests;

[Collection("Postgres")]
public class EventLifecycleTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(EventStatus.Creating)]
    [InlineData(EventStatus.Planning)]
    [InlineData(EventStatus.Applying)]
    [InlineData(EventStatus.Active)]
    [InlineData(EventStatus.Paused)]
    [InlineData(EventStatus.Ending)]
    [InlineData(EventStatus.Failed)]
    public async Task EndOnlyRecordsIntentAndPreservesTheFirstRequest(EventStatus status)
    {
        using var env = new TestEnvironment(postgres);
        var entity = await env.Seed(status, InternalEventStatus.AppliedLaunch);
        using var first = env.Context();
        using var second = env.Context();
        var a = await first.Events.SingleAsync();
        var b = await second.Events.SingleAsync();
        var at = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(await EventLifecycle.RequestEndAsync(first, a, at, default));
        Assert.True(await EventLifecycle.RequestEndAsync(second, b, at.AddMinutes(1), default));
        var saved = await env.Read(entity.Id);
        Assert.Equal(at, saved.EndRequestedAt);
        Assert.Null(saved.EndDate);
        Assert.Equal(status, saved.Status);
        Assert.Equal(InternalEventStatus.AppliedLaunch, saved.InternalStatus);
    }

    [Theory]
    [InlineData(EventStatus.Ended)]
    [InlineData(EventStatus.Expired)]
    public async Task CompletedEventsIgnoreEndRequests(EventStatus status)
    {
        using var env = new TestEnvironment(postgres);
        var entity = await env.Seed(status);
        await env.RequestEnd(entity.Id);
        Assert.Null((await env.Read(entity.Id)).EndRequestedAt);
    }

    [Fact]
    public async Task StaleEditCannotEraseWorkerResourcesOrEndIntent()
    {
        using var env = new TestEnvironment(postgres);
        var entity = await env.Seed();
        using var edit = env.Context();
        await edit.Events.SingleAsync(); // hold the old snapshot before the worker writes
        using (var worker = env.Context())
        {
            var current = await worker.Events.SingleAsync();
            current.WorkspaceId = Guid.NewGuid();
            current.Status = EventStatus.Applying;
            await worker.SaveChangesAsync();
        }
        await env.RequestEnd(entity.Id);
        var before = await env.Read(entity.Id);
        await env.EventService(edit).UpdateAsync(entity.Id, new Alloy.Api.ViewModels.Event
        {
            Name = "Edited", Description = "Description", Status = EventStatus.Ended,
            EndRequestedAt = null, WorkspaceId = null
        }, default);
        var after = await env.Read(entity.Id);
        Assert.Equal("Edited", after.Name);
        Assert.Equal(before.WorkspaceId, after.WorkspaceId);
        Assert.Equal(before.EndRequestedAt, after.EndRequestedAt);
        Assert.Equal(EventStatus.Applying, after.Status);
    }

    [Fact]
    public async Task EndRequestWinsAgainstStaleRedeployScheduling()
    {
        using var env = new TestEnvironment(postgres);
        var entity = await env.Seed();
        using var redeploy = env.Context();
        var stale = await redeploy.Events.SingleAsync();
        await env.RequestEnd(entity.Id);
        Assert.False(await EventLifecycle.ScheduleRedeployAsync(redeploy, stale, default));
        Assert.Equal(EventStatus.Active, (await env.Read(entity.Id)).Status);
    }

    [Fact]
    public async Task EndAfterRedeploySchedulingStillRecordsIntent()
    {
        using var env = new TestEnvironment(postgres);
        var entity = await env.Seed();
        using var end = env.Context();
        var stale = await end.Events.SingleAsync();
        using (var redeploy = env.Context())
        {
            Assert.True(await EventLifecycle.ScheduleRedeployAsync(redeploy, await redeploy.Events.SingleAsync(), default));
        }
        Assert.True(await EventLifecycle.RequestEndAsync(end, stale, DateTime.UtcNow, default));
        Assert.Equal(EventStatus.Planning, (await env.Read(entity.Id)).Status);
    }

    [Theory]
    [InlineData(EventStatus.Active, true)]
    [InlineData(EventStatus.Paused, true)]
    [InlineData(EventStatus.Planning, true)]
    [InlineData(EventStatus.Ending, true)]
    [InlineData(EventStatus.Failed, false)]
    [InlineData(EventStatus.Ended, false)]
    [InlineData(EventStatus.Expired, false)]
    public async Task RecoveryFindsPendingWorkWithoutRevivingFailures(EventStatus status, bool expected)
    {
        using var env = new TestEnvironment(postgres);
        await env.Seed(status);
        using var db = env.Context();
        var entity = await db.Events.SingleAsync();
        entity.EndRequestedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Assert.Equal(expected, await EventLifecycle.UnfinishedEvents(db).AnyAsync());
    }

    [Fact]
    public async Task ExpirationRecordsIntentWithoutErasingTheCurrentRun()
    {
        using var env = new TestEnvironment(postgres);
        var entity = await env.Seed(EventStatus.Applying, InternalEventStatus.AppliedLaunch);
        using var db = env.Context();
        var current = await db.Events.SingleAsync();
        current.RunId = Guid.NewGuid();
        current.ExpirationDate = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        var expired = await EventLifecycle.ExpiredEvents(db, DateTime.UtcNow).SingleAsync();
        await EventLifecycle.RequestEndAsync(db, expired, DateTime.UtcNow, default);
        var saved = await env.Read(entity.Id);
        Assert.Equal(current.RunId, saved.RunId);
        Assert.Equal(EventStatus.Applying, saved.Status);
        Assert.Empty(await EventLifecycle.ExpiredEvents(db, DateTime.UtcNow).ToListAsync());
    }

    [Fact]
    public void EndRequestedAtIsReadableButCannotBeMappedFromAnInput()
    {
        using var env = new TestEnvironment(postgres);
        var timestamp = DateTime.UtcNow;
        var model = env.Mapper.Map<Alloy.Api.ViewModels.Event>(new EventEntity { EndRequestedAt = timestamp });
        Assert.Equal(timestamp, model.EndRequestedAt);
        Assert.Null(env.Mapper.Map<EventEntity>(model).EndRequestedAt);
    }
}
