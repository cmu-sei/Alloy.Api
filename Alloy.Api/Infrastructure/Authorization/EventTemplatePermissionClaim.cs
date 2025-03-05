// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Text.Json;
using Alloy.Api.Data;

namespace Alloy.Api.Infrastructure.Authorization;

public class EventTemplatePermissionClaim
{
    public Guid EventTemplateId { get; set; }
    public EventTemplatePermission[] Permissions { get; set; } = [];

    public EventTemplatePermissionClaim() { }

    public static EventTemplatePermissionClaim FromString(string json)
    {
        return JsonSerializer.Deserialize<EventTemplatePermissionClaim>(json);
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }
}