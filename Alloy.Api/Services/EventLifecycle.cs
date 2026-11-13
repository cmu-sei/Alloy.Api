// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Crucible.Common.EntityEvents.Events;
using Microsoft.EntityFrameworkCore;

namespace Alloy.Api.Services
{
    internal static class EventLifecycle
    {
        private static readonly EventStatus[] LaunchingStatuses =
            [EventStatus.Creating, EventStatus.Planning, EventStatus.Applying];

        // Recovering is included so an end requested while access is being restored is honoured.
        private static readonly EventStatus[] EndAdoptionStatuses =
            [.. LaunchingStatuses, EventStatus.Active, EventStatus.Paused, EventStatus.Recovering];

        // Only a fully launched Event is suspended for a grace period. An end during launch or redeploy
        // has a Terraform run in flight, so there is no stable state to give back; it tears down at once.
        private static readonly EventStatus[] SuspendableStatuses =
            [EventStatus.Active, EventStatus.Paused, EventStatus.Recovering];

        public static bool IsLaunching(EventStatus status) => LaunchingStatuses.Contains(status);

        public static bool CanAdoptEnd(EventStatus status) => EndAdoptionStatuses.Contains(status);

        public static bool CanSuspend(EventStatus status) => SuspendableStatuses.Contains(status);

        /// <summary>Whether the worker has steps left to run for an Event in this status.</summary>
        public static bool IsInProgress(EventStatus status) =>
            IsLaunching(status) || status == EventStatus.Ending || status == EventStatus.Recovering;

        // These conditional updates touch only command-owned fields. They must not save
        // a stale snapshot of the worker's status or the resources it just acquired.
        /// <param name="immediate">
        /// Skip the grace period: <see cref="EventEntity.RecoverableUntil"/> is set to the request time, so
        /// a running Event is not suspended and a Suspended or suspending one goes on to teardown.
        /// </param>
        public static async Task<bool> RequestEndAsync(
            AlloyContext context, EventEntity entity, DateTime requestedAt, CancellationToken ct, bool immediate = false)
        {
            var pending = context.Events
                .Where(x => x.Id == entity.Id && x.EndDate == null &&
                    x.Status != EventStatus.Ended && x.Status != EventStatus.Expired);

            int changed;
            string[] properties;
            if (immediate)
            {
                // Also cuts short an end already requested, so nothing waits out the grace period.
                changed = await pending
                    .Where(x => x.RecoverableUntil == null || x.RecoverableUntil > requestedAt)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.EndRequestedAt, x => x.EndRequestedAt ?? requestedAt)
                        .SetProperty(x => x.RecoverableUntil, requestedAt)
                        .SetProperty(x => x.DateModified, requestedAt), ct);
                properties = [nameof(EventEntity.EndRequestedAt), nameof(EventEntity.RecoverableUntil)];
            }
            else
            {
                changed = await pending
                    .Where(x => x.EndRequestedAt == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.EndRequestedAt, requestedAt)
                        .SetProperty(x => x.DateModified, requestedAt), ct);
                properties = [nameof(EventEntity.EndRequestedAt)];
            }

            await context.Entry(entity).ReloadAsync(ct);
            if (changed != 0)
            {
                // ExecuteUpdate bypasses the change-tracker interceptor.
                await context.PublishEventsAsync(
                    [new EntityUpdated<EventEntity>(entity, properties)], ct);
            }

            return entity.EndDate == null &&
                entity.Status != EventStatus.Ended && entity.Status != EventStatus.Expired;
        }

        public static async Task<bool> ScheduleRedeployAsync(
            AlloyContext context, EventEntity entity, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var changed = await context.Events
                .Where(x => x.Id == entity.Id && x.Status == EventStatus.Active &&
                    x.EndRequestedAt == null && x.EndDate == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, EventStatus.Planning)
                    .SetProperty(x => x.InternalStatus, InternalEventStatus.PlanningRedeploy)
                    .SetProperty(x => x.StatusDate, now)
                    .SetProperty(x => x.DateModified, now)
                    .SetProperty(x => x.ErrorMessage, (string)null)
                    .SetProperty(x => x.ErrorDetail, (string)null)
                    .SetProperty(x => x.LastLaunchStatus, default(EventStatus))
                    .SetProperty(x => x.LastLaunchInternalStatus, default(InternalEventStatus)), ct);

            await context.Entry(entity).ReloadAsync(ct);
            if (changed != 0)
            {
                await context.PublishEventsAsync(
                    [new EntityUpdated<EventEntity>(entity,
                        [nameof(EventEntity.Status), nameof(EventEntity.InternalStatus),
                         nameof(EventEntity.StatusDate), nameof(EventEntity.ErrorMessage),
                         nameof(EventEntity.LastLaunchStatus), nameof(EventEntity.LastLaunchInternalStatus)])], ct);
            }
            return changed != 0;
        }

        /// <summary>
        /// Takes a Suspended Event inside its grace period back to the worker to restore access. Pushes an
        /// expiration that has passed out to <paramref name="durationHours"/> from now, so a recovered
        /// expired Event does not expire again at once. False when the Event is not recoverable now.
        /// </summary>
        public static async Task<bool> RecoverAsync(
            AlloyContext context, EventEntity entity, int durationHours, DateTime now, CancellationToken ct)
        {
            var expiration = now.AddHours(durationHours);
            var changed = await context.Events
                .Where(x => x.Id == entity.Id && x.Status == EventStatus.Suspended &&
                    x.RecoverableUntil != null && x.RecoverableUntil > now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, EventStatus.Recovering)
                    .SetProperty(x => x.InternalStatus, InternalEventStatus.ResumingView)
                    .SetProperty(x => x.EndRequestedAt, (DateTime?)null)
                    .SetProperty(x => x.RecoverableUntil, (DateTime?)null)
                    .SetProperty(x => x.ExpirationDate, x => x.ExpirationDate != null && x.ExpirationDate <= now
                        ? expiration : x.ExpirationDate)
                    .SetProperty(x => x.ErrorMessage, (string)null)
                    .SetProperty(x => x.ErrorDetail, (string)null)
                    .SetProperty(x => x.StatusDate, now)
                    .SetProperty(x => x.DateModified, now), ct);

            await context.Entry(entity).ReloadAsync(ct);
            if (changed != 0)
            {
                await context.PublishEventsAsync(
                    [new EntityUpdated<EventEntity>(entity,
                        [nameof(EventEntity.Status), nameof(EventEntity.InternalStatus),
                         nameof(EventEntity.EndRequestedAt), nameof(EventEntity.RecoverableUntil),
                         nameof(EventEntity.ExpirationDate), nameof(EventEntity.ErrorMessage),
                         nameof(EventEntity.StatusDate)])], ct);
            }
            return changed != 0;
        }

        /// <summary>
        /// Hands a Suspended Event whose grace period has passed to teardown. Conditional, so it cannot
        /// overwrite a recover that won the race. False when the Event was not due.
        /// </summary>
        public static async Task<bool> BeginTeardownAsync(
            AlloyContext context, EventEntity entity, DateTime now, CancellationToken ct)
        {
            var changed = await context.Events
                .Where(x => x.Id == entity.Id && x.Status == EventStatus.Suspended && x.RecoverableUntil <= now)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, EventStatus.Ending)
                    .SetProperty(x => x.InternalStatus, InternalEventStatus.EndQueued)
                    .SetProperty(x => x.StatusDate, now)
                    .SetProperty(x => x.DateModified, now), ct);

            await context.Entry(entity).ReloadAsync(ct);
            if (changed != 0)
            {
                await context.PublishEventsAsync(
                    [new EntityUpdated<EventEntity>(entity,
                        [nameof(EventEntity.Status), nameof(EventEntity.InternalStatus), nameof(EventEntity.StatusDate)])], ct);
            }
            return changed != 0;
        }

        public static IQueryable<EventEntity> UnfinishedEvents(AlloyContext context) =>
            context.Events.Where(x =>
                LaunchingStatuses.Contains(x.Status) || x.Status == EventStatus.Ending ||
                x.Status == EventStatus.Recovering ||
                ((x.Status == EventStatus.Active || x.Status == EventStatus.Paused) &&
                    x.EndRequestedAt != null && x.EndDate == null));

        public static IQueryable<EventEntity> ExpiredEvents(AlloyContext context, DateTime now) =>
            context.Events.Where(x => x.EndRequestedAt == null && x.EndDate == null &&
                x.ExpirationDate < now && EndAdoptionStatuses.Contains(x.Status));

        public static IQueryable<EventEntity> LapsedSuspensions(AlloyContext context, DateTime now) =>
            context.Events.Where(x => x.Status == EventStatus.Suspended && x.RecoverableUntil <= now);
    }
}
