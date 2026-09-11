// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Alloy.Api.ViewModels
{
    /// <summary>
    /// The full diagnostic text behind a failed Event, kept off the Event view model on purpose:
    /// it can hold Terraform output naming internal hostnames, addresses and variable values, and
    /// the Event view model is broadcast over SignalR to every member of the Event.
    /// Only reachable through GET /api/events/{id}/error-detail, which requires the system-wide
    /// ManageEvents permission.
    /// </summary>
    public class EventErrorDetail
    {
        public Guid EventId { get; set; }

        /// <summary>
        /// The same short summary carried on the Event view model, repeated here so a caller
        /// rendering the detail does not have to correlate two responses.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// ANSI-stripped and truncated to the last EventErrorLimits.MaxDetailLength characters -
        /// Terraform prints the actual error last. Null when the Event has not failed, or when the
        /// failure had nothing more to say than the summary.
        /// </summary>
        public string ErrorDetail { get; set; }
    }
}
