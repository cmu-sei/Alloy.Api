// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Alloy.Api.Services
{
    public interface ITelemetryService
    {
    }

    public class TelemetryService : ITelemetryService
    {
        public const string AlloyMeterName = "cmu_sei_crucible_alloy";
        public readonly Meter AlloyMeter = new Meter(AlloyMeterName, "1.0");
        public Gauge<int> ActiveEvents;
        public Gauge<int> EndedEvents;
        public Gauge<int> FailedEvents;
        public Gauge<int> FailedEventsWithResources;

        public TelemetryService()
        {
            ActiveEvents = AlloyMeter.CreateGauge<int>("alloy_active_events");
            EndedEvents = AlloyMeter.CreateGauge<int>("alloy_ended_events");
            FailedEvents = AlloyMeter.CreateGauge<int>("alloy_failed_events");
            // Failed Events that still hold a Caster Workspace, Player View or Steamfitter Scenario
            // are real orphans in the range. Bootstrap retries them on restart, but anything that
            // sticks here needs an operator to look at it, so it is worth alerting on.
            FailedEventsWithResources = AlloyMeter.CreateGauge<int>("alloy_failed_events_with_resources");
        }

        public async Task UpdateEventGauges(AlloyContext alloyContext, CancellationToken ct)
        {
            var activeEventCount = await alloyContext.Events.CountAsync(m => m.Status == EventStatus.Active);
            var endedEventCount = await alloyContext.Events.CountAsync(m => m.Status == EventStatus.Ended);
            var failedEventCount = await alloyContext.Events.CountAsync(m => m.Status == EventStatus.Failed);
            var failedWithResourcesCount = await alloyContext.Events.CountAsync(m =>
                m.Status == EventStatus.Failed &&
                (m.WorkspaceId != null || m.ViewId != null || m.ScenarioId != null));
            ActiveEvents.Record(activeEventCount);
            EndedEvents.Record(endedEventCount);
            FailedEvents.Record(failedEventCount);
            FailedEventsWithResources.Record(failedWithResourcesCount);
        }

    }
}
