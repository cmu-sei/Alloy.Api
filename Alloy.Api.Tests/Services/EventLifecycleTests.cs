// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Services;
using Alloy.Api.Tests.Support;
using Crucible.Common.EntityEvents.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Alloy.Api.Tests.Services;

/// <summary>
/// <see cref="EventLifecycle"/>'s conditional updates: an end request and a redeploy each touch only the
/// fields they own, so a request racing the worker cannot erase what the worker wrote, and a request the
/// worker has not seen yet survives a restart.
/// </summary>
public class EventLifecycleTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly DateTime RequestedAt = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>The second of two racing end requests keeps the first one's timestamp and the worker's status.</summary>
    [Theory]
    [InlineData(EventStatus.Creating)]
    [InlineData(EventStatus.Planning)]
    [InlineData(EventStatus.Applying)]
    [InlineData(EventStatus.Active)]
    [InlineData(EventStatus.Paused)]
    [InlineData(EventStatus.Ending)]
    [InlineData(EventStatus.Failed)]
    public async Task RequestEnd_records_only_the_intent_and_keeps_the_first_request(EventStatus status)
    {
        var entity = await SeedEvent(status, InternalEventStatus.AppliedLaunch);
        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.Events.SingleAsync(x => x.Id == entity.Id, Ct);
        var b = await second.Events.SingleAsync(x => x.Id == entity.Id, Ct);
        await EventLifecycle.RequestEndAsync(first, a, RequestedAt, Ct);

        var accepted = await EventLifecycle.RequestEndAsync(second, b, RequestedAt.AddMinutes(1), Ct);

        Assert.True(accepted);
        var saved = await Stored(entity.Id);
        Assert.Equal(RequestedAt, saved.EndRequestedAt);
        Assert.Null(saved.EndDate);
        Assert.Equal(status, saved.Status);
        Assert.Equal(InternalEventStatus.AppliedLaunch, saved.InternalStatus);
    }

    [Theory]
    [InlineData(EventStatus.Ended)]
    [InlineData(EventStatus.Expired)]
    public async Task RequestEnd_ignores_an_event_that_already_completed(EventStatus status)
    {
        var entity = await SeedEvent(status, InternalEventStatus.Ended);
        await using var context = NewContext();
        var row = await context.Events.SingleAsync(x => x.Id == entity.Id, Ct);

        var accepted = await EventLifecycle.RequestEndAsync(context, row, RequestedAt, Ct);

        Assert.False(accepted);
        Assert.Null((await Stored(entity.Id)).EndRequestedAt);
    }

    /// <summary>ExecuteUpdate bypasses the interceptor, so the change is published by hand.</summary>
    [Fact]
    public async Task RequestEnd_publishes_the_change_to_EndRequestedAt()
    {
        var entity = await SeedEvent(EventStatus.Active, InternalEventStatus.Launched);
        await using var context = NewContext();
        var row = await context.Events.SingleAsync(x => x.Id == entity.Id, Ct);
        Mediator.ClearReceivedCalls();

        await EventLifecycle.RequestEndAsync(context, row, RequestedAt, Ct);

        await Mediator.Received(1).Publish(
            Arg.Is<INotification>(x => x is EntityUpdated<EventEntity>
                && ((EntityUpdated<EventEntity>)x).ModifiedProperties.Length == 1
                && ((EntityUpdated<EventEntity>)x).ModifiedProperties[0] == nameof(EventEntity.EndRequestedAt)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScheduleRedeploy_loses_to_an_end_request_made_after_the_event_was_read()
    {
        var entity = await SeedEvent(EventStatus.Active, InternalEventStatus.Launched);
        await using var redeploy = NewContext();
        var stale = await redeploy.Events.SingleAsync(x => x.Id == entity.Id, Ct);
        await using (var end = NewContext())
        {
            await EventLifecycle.RequestEndAsync(end, await end.Events.SingleAsync(x => x.Id == entity.Id, Ct), RequestedAt, Ct);
        }

        var scheduled = await EventLifecycle.ScheduleRedeployAsync(redeploy, stale, Ct);

        Assert.False(scheduled);
        Assert.Equal(EventStatus.Active, (await Stored(entity.Id)).Status);
    }

    [Fact]
    public async Task RequestEnd_after_a_redeploy_was_scheduled_still_records_the_intent()
    {
        var entity = await SeedEvent(EventStatus.Active, InternalEventStatus.Launched);
        await using var end = NewContext();
        var stale = await end.Events.SingleAsync(x => x.Id == entity.Id, Ct);
        await using (var redeploy = NewContext())
        {
            await EventLifecycle.ScheduleRedeployAsync(redeploy, await redeploy.Events.SingleAsync(x => x.Id == entity.Id, Ct), Ct);
        }

        var accepted = await EventLifecycle.RequestEndAsync(end, stale, RequestedAt, Ct);

        Assert.True(accepted);
        var saved = await Stored(entity.Id);
        Assert.Equal(EventStatus.Planning, saved.Status);
        Assert.Equal(RequestedAt, saved.EndRequestedAt);
    }

    /// <summary>A recovery scan picks up pending end requests, but never revives a failure or a completed event.</summary>
    [Theory]
    [InlineData(EventStatus.Active, true)]
    [InlineData(EventStatus.Paused, true)]
    [InlineData(EventStatus.Planning, true)]
    [InlineData(EventStatus.Ending, true)]
    [InlineData(EventStatus.Failed, false)]
    [InlineData(EventStatus.Ended, false)]
    [InlineData(EventStatus.Expired, false)]
    public async Task UnfinishedEvents_finds_pending_work_without_reviving_failures(EventStatus status, bool expected)
    {
        var entity = TestData.Event(null, status);
        entity.EndRequestedAt = RequestedAt;
        await Seed(entity);
        await using var context = NewContext();

        var found = await EventLifecycle.UnfinishedEvents(context).AnyAsync(x => x.Id == entity.Id, Ct);

        Assert.Equal(expected, found);
    }

    [Fact]
    public async Task An_expired_event_records_the_end_intent_without_erasing_the_current_run()
    {
        var entity = TestData.Event(null, EventStatus.Applying, InternalEventStatus.AppliedLaunch);
        entity.RunId = Guid.NewGuid();
        entity.ExpirationDate = DateTime.UtcNow.AddMinutes(-1);
        await Seed(entity);
        await using var context = NewContext();
        var expired = await EventLifecycle.ExpiredEvents(context, DateTime.UtcNow).SingleAsync(Ct);

        await EventLifecycle.RequestEndAsync(context, expired, DateTime.UtcNow, Ct);

        var saved = await Stored(entity.Id);
        Assert.Equal(entity.RunId, saved.RunId);
        Assert.Equal(EventStatus.Applying, saved.Status);
        await using var after = NewContext();
        Assert.Empty(await EventLifecycle.ExpiredEvents(after, DateTime.UtcNow).ToListAsync(Ct));
    }

    private async Task<EventEntity> SeedEvent(EventStatus status, InternalEventStatus internalStatus)
    {
        var template = TestData.EventTemplate();
        var entity = TestData.Event(template.Id, status, internalStatus);
        await Seed(template, entity);

        return entity;
    }

    private async Task<EventEntity> Stored(Guid id)
    {
        await using var context = NewContext();

        return await context.Events.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }
}
