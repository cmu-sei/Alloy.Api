// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;

namespace Alloy.Api.ViewModels
{
    public class Event : Base, IAuthorizationType
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; }
        public Guid? EventTemplateId { get; set; }
        public Guid? ViewId { get; set; }
        public Guid? WorkspaceId { get; set; }
        public Guid? RunId { get; set; }
        public Guid? ScenarioId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string ShareCode { get; set; }
        public List<EventUser> EventUsers { get; set; }
        public EventStatus Status { get; set; }
        public InternalEventStatus InternalStatus { get; set; }
        public int FailureCount { get; set; }
        public EventStatus LastLaunchStatus { get; set; }
        public InternalEventStatus LastLaunchInternalStatus { get; set; }
        public EventStatus LastEndStatus { get; set; }
        public InternalEventStatus LastEndInternalStatus { get; set; }
        public DateTime StatusDate { get; set; }
        public DateTime? LaunchDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public IEnumerable<string> EventPermissions { get; set; }
    }
}
