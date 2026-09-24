// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Alloy.Api.Data;

namespace Alloy.Api.ViewModels
{
    /// <summary>Initial values for direct event creation. Template launches use CreateEventCommand.</summary>
    public class CreateEventRequest
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; }
        public Guid? EventTemplateId { get; set; }
        public Guid? ViewId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string ShareCode { get; set; }
        public EventStatus Status { get; set; }
        public InternalEventStatus InternalStatus { get; set; }
        public DateTime StatusDate { get; set; }
        public DateTime? LaunchDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime? ExpirationDate { get; set; }
    }
}
