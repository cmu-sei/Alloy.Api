// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Alloy.Api.ViewModels
{
    /// <summary>Fields editable on an existing event.</summary>
    public class UpdateEventRequest
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime? ExpirationDate { get; set; }
    }
}
