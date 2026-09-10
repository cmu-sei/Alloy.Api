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

        private static readonly EventStatus[] EndAdoptionStatuses =
            [.. LaunchingStatuses, EventStatus.Active, EventStatus.Paused];

        public static bool IsLaunching(EventStatus status) => LaunchingStatuses.Contains(status);

        public static bool CanAdoptEnd(EventStatus status) => EndAdoptionStatuses.Contains(status);

        // These conditional updates touch only command-owned fields. They must not save
        // a stale snapshot of the worker's status or the resources it just acquired.
        public static async Task<bool> RequestEndAsync(
            AlloyContext context, EventEntity entity, DateTime requestedAt, CancellationToken ct)
        {
            var changed = await context.Events
                .Where(x => x.Id == entity.Id && x.EndRequestedAt == null && x.EndDate == null &&
                    x.Status != EventStatus.Ended && x.Status != EventStatus.Expired)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.EndRequestedAt, requestedAt)
                    .SetProperty(x => x.DateModified, requestedAt), ct);

            await context.Entry(entity).ReloadAsync(ct);
            if (changed != 0)
            {
                // ExecuteUpdate bypasses the change-tracker interceptor.
                await context.PublishEventsAsync(
                    [new EntityUpdated<EventEntity>(entity, [nameof(EventEntity.EndRequestedAt)])], ct);
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

        public static IQueryable<EventEntity> UnfinishedEvents(AlloyContext context) =>
            context.Events.Where(x =>
                LaunchingStatuses.Contains(x.Status) || x.Status == EventStatus.Ending ||
                ((x.Status == EventStatus.Active || x.Status == EventStatus.Paused) &&
                    x.EndRequestedAt != null && x.EndDate == null));

        public static IQueryable<EventEntity> ExpiredEvents(AlloyContext context, DateTime now) =>
            context.Events.Where(x => x.EndRequestedAt == null && x.EndDate == null &&
                x.ExpirationDate < now && EndAdoptionStatuses.Contains(x.Status));
    }
}
