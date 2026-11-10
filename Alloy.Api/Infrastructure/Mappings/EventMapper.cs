// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Alloy.Api.Data.Models;
using Alloy.Api.ViewModels;
using Riok.Mapperly.Abstractions;

namespace Alloy.Api.Infrastructure.Mappings;

/// <summary>Events' mappings.</summary>
/// <remarks>
/// <see cref="EventEntity.ErrorDetail"/> can hold internal hostnames, so only the error-detail
/// endpoint returns it; the view model has no such member.
/// </remarks>
[Mapper]
public static partial class EventMapper
{
    /// <remarks><see cref="Event.EventPermissions"/> describes the caller; the controller fills it.</remarks>
    [MapperIgnoreTarget(nameof(Event.EventPermissions))]
    public static partial Event ToEvent(this EventEntity source);
}
