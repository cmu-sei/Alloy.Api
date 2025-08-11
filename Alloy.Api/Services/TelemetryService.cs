// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Diagnostics.Metrics;

namespace Alloy.Api.Services
{
    public interface ITelemetryService
    {
    }

    public class TelemetryService : ITelemetryService
    {
        public const string CasterMeterName = "cmu_sei_crucible_alloy";
        public readonly Meter CasterMeter = new Meter(CasterMeterName, "1.0");
        public Gauge<int> ActiveEvents;
        public Gauge<int> EndedEvents;

        public TelemetryService()
        {
            ActiveEvents = CasterMeter.CreateGauge<int>("alloy_active_events");
            EndedEvents = CasterMeter.CreateGauge<int>("alloy_ended_events");
        }

    }
}
