// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Text.Json;
using Alloy.Api.Data;

namespace Alloy.Api.Infrastructure.Authorization;

public class EventPermissionClaim
{
    public Guid EventId { get; set; }
    public EventPermission[] Permissions { get; set; } = [];

    public EventPermissionClaim() { }

    public static EventPermissionClaim FromString(string json)
    {
        return JsonSerializer.Deserialize<EventPermissionClaim>(json);
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }
}