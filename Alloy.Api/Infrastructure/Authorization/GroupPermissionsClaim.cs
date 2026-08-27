// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Text.Json;
using Alloy.Api.Data;

namespace Alloy.Api.Infrastructure.Authorization;

public class GroupPermissionsClaim
{
    public Guid GroupId { get; set; }
    public GroupPermission[] Permissions { get; set; } = [];

    public GroupPermissionsClaim() { }

    public static GroupPermissionsClaim FromString(string json)
    {
        return JsonSerializer.Deserialize<GroupPermissionsClaim>(json);
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }
}
