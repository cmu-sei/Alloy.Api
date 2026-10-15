// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Alloy.Api.Data.Models;
using Alloy.Api.Tests.Support;
using Alloy.Api.ViewModels;

namespace Alloy.Api.Tests.Infrastructure.Mappings;

/// <summary>The real AutoMapper configuration, as <see cref="TestMapper"/> builds it from Startup's.</summary>
public class EventProfileTests
{
    [Fact]
    public void An_events_end_request_time_is_on_its_view_model()
    {
        var requestedAt = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

        var model = TestMapper.Mapper.Map<Event>(new EventEntity { EndRequestedAt = requestedAt });

        Assert.Equal(requestedAt, model.EndRequestedAt);
    }

    /// <summary><c>ErrorDetail</c> can hold internal hostnames, so only the error-detail endpoint returns it.</summary>
    [Fact]
    public void An_events_error_detail_is_not_on_its_view_model()
    {
        Assert.Null(typeof(Event).GetProperty(nameof(EventEntity.ErrorDetail)));
    }
}
