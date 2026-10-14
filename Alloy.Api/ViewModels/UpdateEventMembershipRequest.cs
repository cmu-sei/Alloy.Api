// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Alloy.Api.ViewModels
{
    /// <summary>
    /// Fields editable on an existing event membership. It has no EventId, so a request
    /// cannot name a different event than the one the membership belongs to.
    /// </summary>
    public class UpdateEventMembershipRequest
    {
        public Guid RoleId { get; set; }
    }
}
