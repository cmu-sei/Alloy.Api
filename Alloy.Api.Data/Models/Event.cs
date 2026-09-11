// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Alloy.Api.Data.Models
{
    public class EventEntity : BaseEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; }
        public Guid? EventTemplateId { get; set; }
        public virtual EventTemplateEntity EventTemplate { get; set; }
        public Guid? ViewId { get; set; }
        public Guid? WorkspaceId { get; set; }
        public Guid? RunId { get; set; }
        public Guid? ScenarioId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string ShareCode { get; set; }
        public EventStatus Status { get; set; }
        public InternalEventStatus InternalStatus { get; set; }
        public int FailureCount { get; set; }
        public EventStatus LastLaunchStatus { get; set; }
        public InternalEventStatus LastLaunchInternalStatus { get; set; }
        public EventStatus LastEndStatus { get; set; }
        public InternalEventStatus LastEndInternalStatus { get; set; }

        /// <summary>
        /// A short description of why the Event failed, safe to show to any user. Capped at
        /// <see cref="EventErrorLimits.MaxSummaryLength"/> characters. Exposed on the Event view
        /// model, so it flows out over SignalR to everyone who can see the Event.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// The full diagnostic text behind <see cref="ErrorMessage"/> - Terraform plan/apply output,
        /// an API response body - ANSI-stripped and truncated to
        /// <see cref="EventErrorLimits.MaxDetailLength"/> characters keeping the tail. Deliberately
        /// *not* on the Event view model: it can be hundreds of KB and can contain internal host
        /// names, addresses and variable values. Read it through GET /api/events/{id}/error-detail,
        /// which requires the system-wide ManageEvents permission.
        /// </summary>
        public string ErrorDetail { get; set; }

        public DateTime StatusDate { get; set; }
        public DateTime? LaunchDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime? EndRequestedAt { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public virtual ICollection<EventMembershipEntity> Memberships { get; set; } = new List<EventMembershipEntity>();
    }
}
