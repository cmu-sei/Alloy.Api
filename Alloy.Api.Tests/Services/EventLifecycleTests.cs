// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
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
/// worker has not seen yet survives a restart. A recover and the teardown of a lapsed suspension race the
/// same way, and the grace period decides which of them wins.
/// </summary>
public class EventLifecycleTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly DateTime RequestedAt = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>The clock the grace-period updates are given, an hour after <see cref="RequestedAt"/>.</summary>
    private static readonly DateTime Now = new(2026, 9, 10, 13, 0, 0, DateTimeKind.Utc);

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

    /// <summary>An immediate end leaves no grace: the event is recoverable only until the moment it was ended.</summary>
    [Fact]
    public async Task RequestEnd_immediately_ends_the_grace_at_the_request_time()
    {
        var entity = await SeedEvent(EventStatus.Active, InternalEventStatus.Launched);
        await using var context = NewContext();
        var row = await context.Events.SingleAsync(x => x.Id == entity.Id, Ct);

        var accepted = await EventLifecycle.RequestEndAsync(context, row, Now, Ct, immediate: true);

        Assert.True(accepted);
        var saved = await Stored(entity.Id);
        Assert.Equal((Now, Now), (saved.EndRequestedAt, saved.RecoverableUntil));
        Assert.Equal((EventStatus.Active, InternalEventStatus.Launched), (saved.Status, saved.InternalStatus));
        Assert.Null(saved.EndDate);
    }

    /// <summary>Cutting a suspension short keeps when the event was first ended, and moves only the end of its grace.</summary>
    [Fact]
    public async Task RequestEnd_immediately_keeps_an_earlier_request_and_cuts_its_grace_short()
    {
        var entity = await SeedSuspension(Now.AddMinutes(10));
        await using var context = NewContext();
        var row = await context.Events.SingleAsync(x => x.Id == entity.Id, Ct);

        var accepted = await EventLifecycle.RequestEndAsync(context, row, Now, Ct, immediate: true);

        Assert.True(accepted);
        var saved = await Stored(entity.Id);
        Assert.Equal((RequestedAt, Now), (saved.EndRequestedAt, saved.RecoverableUntil));
        Assert.Equal((EventStatus.Suspended, InternalEventStatus.Suspended), (saved.Status, saved.InternalStatus));
    }

    /// <summary>A grace that has already passed is not moved later by an immediate end made after it.</summary>
    [Fact]
    public async Task RequestEnd_immediately_leaves_a_grace_that_has_already_passed()
    {
        var entity = await SeedSuspension(Now.AddMinutes(-5));
        await using var context = NewContext();
        var row = await context.Events.SingleAsync(x => x.Id == entity.Id, Ct);

        await EventLifecycle.RequestEndAsync(context, row, Now, Ct, immediate: true);

        var saved = await Stored(entity.Id);
        Assert.Equal((RequestedAt, Now.AddMinutes(-5)), (saved.EndRequestedAt, saved.RecoverableUntil));
    }

    [Theory]
    [InlineData(EventStatus.Ended)]
    [InlineData(EventStatus.Expired)]
    public async Task RequestEnd_immediately_ignores_an_event_that_already_completed(EventStatus status)
    {
        var entity = await SeedEvent(status, InternalEventStatus.Ended);
        await using var context = NewContext();
        var row = await context.Events.SingleAsync(x => x.Id == entity.Id, Ct);

        var accepted = await EventLifecycle.RequestEndAsync(context, row, Now, Ct, immediate: true);

        Assert.False(accepted);
        var saved = await Stored(entity.Id);
        Assert.Equal((null, null), (saved.EndRequestedAt, saved.RecoverableUntil));
    }

    /// <summary>ExecuteUpdate bypasses the interceptor, so both changed fields are published by hand.</summary>
    [Fact]
    public async Task RequestEnd_immediately_publishes_the_change_to_EndRequestedAt_and_RecoverableUntil()
    {
        var entity = await SeedEvent(EventStatus.Active, InternalEventStatus.Launched);
        await using var context = NewContext();
        var row = await context.Events.SingleAsync(x => x.Id == entity.Id, Ct);
        Mediator.ClearReceivedCalls();

        await EventLifecycle.RequestEndAsync(context, row, Now, Ct, immediate: true);

        await Mediator.Received(1).Publish(
            Arg.Is<INotification>(x => x is EntityUpdated<EventEntity>
                && ((EntityUpdated<EventEntity>)x).ModifiedProperties.Length == 2
                && ((EntityUpdated<EventEntity>)x).ModifiedProperties.Contains(nameof(EventEntity.EndRequestedAt))
                && ((EntityUpdated<EventEntity>)x).ModifiedProperties.Contains(nameof(EventEntity.RecoverableUntil))),
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
    [InlineData(EventStatus.Creating, true)]
    [InlineData(EventStatus.Planning, true)]
    [InlineData(EventStatus.Applying, true)]
    [InlineData(EventStatus.Active, true)]
    [InlineData(EventStatus.Paused, true)]
    [InlineData(EventStatus.Ending, true)]
    [InlineData(EventStatus.Failed, false)]
    [InlineData(EventStatus.Ended, false)]
    [InlineData(EventStatus.Expired, false)]
    [InlineData(EventStatus.Suspended, false)]
    [InlineData(EventStatus.Recovering, true)]
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

    /// <summary>The query service hands these to teardown; one inside its grace, or not suspended, is not due.</summary>
    [Theory]
    [InlineData(EventStatus.Suspended, -1, true)]
    [InlineData(EventStatus.Suspended, 0, true)]
    [InlineData(EventStatus.Suspended, 1, false)]
    [InlineData(EventStatus.Ending, -1, false)]
    [InlineData(EventStatus.Recovering, -1, false)]
    [InlineData(EventStatus.Ended, -1, false)]
    public async Task LapsedSuspensions_finds_only_suspended_events_whose_grace_has_passed(EventStatus status, int minutesFromNow, bool expected)
    {
        var entity = await SeedSuspension(Now.AddMinutes(minutesFromNow), status);
        await using var context = NewContext();

        var found = await EventLifecycle.LapsedSuspensions(context, Now).AnyAsync(x => x.Id == entity.Id, Ct);

        Assert.Equal(expected, found);
    }

    [Fact]
    public async Task BeginTeardown_hands_a_suspension_whose_grace_has_passed_to_teardown_keeping_its_resources()
    {
        var entity = await SeedSuspension(Now.AddMinutes(-1));
        await using var context = NewContext();
        var row = await context.Events.SingleAsync(x => x.Id == entity.Id, Ct);

        var started = await EventLifecycle.BeginTeardownAsync(context, row, Now, Ct);

        Assert.True(started);
        var saved = await Stored(entity.Id);
        Assert.Equal((EventStatus.Ending, InternalEventStatus.EndQueued), (saved.Status, saved.InternalStatus));
        Assert.Equal(Now, saved.StatusDate);
        Assert.Equal((entity.ViewId, entity.WorkspaceId, entity.ScenarioId), (saved.ViewId, saved.WorkspaceId, saved.ScenarioId));
        Assert.Equal(RequestedAt, saved.EndRequestedAt);
        Assert.Null(saved.EndDate);
        Assert.Equal(EventStatus.Ending, row.Status);
    }

    [Fact]
    public async Task BeginTeardown_leaves_a_suspension_inside_its_grace_alone()
    {
        var entity = await SeedSuspension(Now.AddMinutes(1));
        await using var context = NewContext();
        var row = await context.Events.SingleAsync(x => x.Id == entity.Id, Ct);

        var started = await EventLifecycle.BeginTeardownAsync(context, row, Now, Ct);

        Assert.False(started);
        var saved = await Stored(entity.Id);
        Assert.Equal((EventStatus.Suspended, InternalEventStatus.Suspended), (saved.Status, saved.InternalStatus));
        Assert.Equal(TestData.DefaultDate, saved.StatusDate);
    }

    /// <summary>A worker that read the suspension before a recover landed must not tear the recovered event down.</summary>
    [Fact]
    public async Task BeginTeardown_loses_to_a_recover_made_after_the_event_was_read()
    {
        var entity = await SeedSuspension(Now.AddMinutes(-1));
        await using var teardown = NewContext();
        var stale = await teardown.Events.SingleAsync(x => x.Id == entity.Id, Ct);
        await using (var recover = NewContext())
        {
            await EventLifecycle.RecoverAsync(recover, await recover.Events.SingleAsync(x => x.Id == entity.Id, Ct), 1, Now.AddMinutes(-2), Ct);
        }

        var started = await EventLifecycle.BeginTeardownAsync(teardown, stale, Now, Ct);

        Assert.False(started);
        var saved = await Stored(entity.Id);
        Assert.Equal((EventStatus.Recovering, InternalEventStatus.ResumingView), (saved.Status, saved.InternalStatus));
        Assert.Equal(EventStatus.Recovering, stale.Status);
    }

    [Fact]
    public async Task Recover_hands_a_suspension_inside_its_grace_to_the_worker_and_clears_the_end()
    {
        var entity = await SeedSuspension(Now.AddMinutes(10), configure: x =>
        {
            x.ErrorMessage = "The event was ended, but its Player view could not be hidden.";
            x.ErrorDetail = "Player answered 503.";
        });
        await using var context = NewContext();
        var row = await context.Events.SingleAsync(x => x.Id == entity.Id, Ct);

        var recovered = await EventLifecycle.RecoverAsync(context, row, 4, Now, Ct);

        Assert.True(recovered);
        var saved = await Stored(entity.Id);
        Assert.Equal((EventStatus.Recovering, InternalEventStatus.ResumingView), (saved.Status, saved.InternalStatus));
        Assert.Equal((null, null), (saved.EndRequestedAt, saved.RecoverableUntil));
        Assert.Equal((null, null), (saved.ErrorMessage, saved.ErrorDetail));
        Assert.Equal(Now, saved.StatusDate);
        Assert.Equal((entity.ViewId, entity.WorkspaceId, entity.ScenarioId), (saved.ViewId, saved.WorkspaceId, saved.ScenarioId));
        Assert.Null(saved.EndDate);
        Assert.Equal(EventStatus.Recovering, row.Status);
    }

    /// <summary>An event that expired into its grace period would otherwise expire again as soon as it was back.</summary>
    [Fact]
    public async Task Recover_pushes_a_passed_expiration_out_by_the_templates_duration()
    {
        var entity = await SeedSuspension(Now.AddMinutes(10), configure: x => x.ExpirationDate = Now.AddMinutes(-20));
        await using var context = NewContext();
        var row = await context.Events.SingleAsync(x => x.Id == entity.Id, Ct);

        await EventLifecycle.RecoverAsync(context, row, 4, Now, Ct);

        Assert.Equal(Now.AddHours(4), (await Stored(entity.Id)).ExpirationDate);
    }

    [Fact]
    public async Task Recover_keeps_an_expiration_that_has_not_passed()
    {
        var entity = await SeedSuspension(Now.AddMinutes(10), configure: x => x.ExpirationDate = Now.AddMinutes(20));
        await using var context = NewContext();
        var row = await context.Events.SingleAsync(x => x.Id == entity.Id, Ct);

        await EventLifecycle.RecoverAsync(context, row, 4, Now, Ct);

        Assert.Equal(Now.AddMinutes(20), (await Stored(entity.Id)).ExpirationDate);
    }

    [Fact]
    public async Task Recover_gives_no_expiration_to_an_event_that_had_none()
    {
        var entity = await SeedSuspension(Now.AddMinutes(10));
        await using var context = NewContext();
        var row = await context.Events.SingleAsync(x => x.Id == entity.Id, Ct);

        await EventLifecycle.RecoverAsync(context, row, 4, Now, Ct);

        Assert.Null((await Stored(entity.Id)).ExpirationDate);
    }

    /// <summary>The grace period ends at <c>RecoverableUntil</c> itself: from then on the event belongs to teardown.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Recover_loses_to_a_grace_period_that_has_passed(int minutesFromNow)
    {
        var entity = await SeedSuspension(Now.AddMinutes(minutesFromNow));
        await using var context = NewContext();
        var row = await context.Events.SingleAsync(x => x.Id == entity.Id, Ct);

        var recovered = await EventLifecycle.RecoverAsync(context, row, 4, Now, Ct);

        Assert.False(recovered);
        var saved = await Stored(entity.Id);
        Assert.Equal((EventStatus.Suspended, InternalEventStatus.Suspended), (saved.Status, saved.InternalStatus));
        Assert.Equal((RequestedAt, Now.AddMinutes(minutesFromNow)), (saved.EndRequestedAt, saved.RecoverableUntil));
    }

    [Theory]
    [InlineData(EventStatus.Active)]
    [InlineData(EventStatus.Ending)]
    [InlineData(EventStatus.Recovering)]
    [InlineData(EventStatus.Ended)]
    public async Task Recover_refuses_an_event_that_is_not_suspended(EventStatus status)
    {
        var entity = await SeedSuspension(Now.AddMinutes(10), status);
        await using var context = NewContext();
        var row = await context.Events.SingleAsync(x => x.Id == entity.Id, Ct);

        var recovered = await EventLifecycle.RecoverAsync(context, row, 4, Now, Ct);

        Assert.False(recovered);
        var saved = await Stored(entity.Id);
        Assert.Equal(status, saved.Status);
        Assert.Equal((RequestedAt, Now.AddMinutes(10)), (saved.EndRequestedAt, saved.RecoverableUntil));
    }

    /// <summary>ExecuteUpdate bypasses the interceptor, so the change is published by hand.</summary>
    [Fact]
    public async Task Recover_publishes_the_change_to_the_status()
    {
        var entity = await SeedSuspension(Now.AddMinutes(10));
        await using var context = NewContext();
        var row = await context.Events.SingleAsync(x => x.Id == entity.Id, Ct);
        Mediator.ClearReceivedCalls();

        await EventLifecycle.RecoverAsync(context, row, 4, Now, Ct);

        await Mediator.Received(1).Publish(
            Arg.Is<INotification>(x => x is EntityUpdated<EventEntity>
                && ((EntityUpdated<EventEntity>)x).Entity.Id == entity.Id
                && ((EntityUpdated<EventEntity>)x).ModifiedProperties.Contains(nameof(EventEntity.Status))
                && ((EntityUpdated<EventEntity>)x).ModifiedProperties.Contains(nameof(EventEntity.RecoverableUntil))),
            Arg.Any<CancellationToken>());
    }

    private async Task<EventEntity> SeedEvent(EventStatus status, InternalEventStatus internalStatus)
    {
        var template = TestData.EventTemplate();
        var entity = TestData.Event(template.Id, status, internalStatus);
        await Seed(template, entity);

        return entity;
    }

    /// <summary>
    /// An event the worker suspended at <see cref="RequestedAt"/>, recoverable until <paramref name="recoverableUntil"/>,
    /// with its view, workspace and scenario still in place.
    /// </summary>
    private async Task<EventEntity> SeedSuspension(
        DateTime recoverableUntil,
        EventStatus status = EventStatus.Suspended,
        Action<EventEntity> configure = null)
    {
        var template = TestData.EventTemplate();
        var entity = TestData.Event(template.Id, status, status == EventStatus.Suspended ? InternalEventStatus.Suspended : InternalEventStatus.Launched);
        entity.EndRequestedAt = RequestedAt;
        entity.RecoverableUntil = recoverableUntil;
        entity.ViewId = Guid.NewGuid();
        entity.WorkspaceId = Guid.NewGuid();
        entity.ScenarioId = Guid.NewGuid();
        configure?.Invoke(entity);
        await Seed(template, entity);

        return entity;
    }

    private async Task<EventEntity> Stored(Guid id)
    {
        await using var context = NewContext();

        return await context.Events.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }
}
