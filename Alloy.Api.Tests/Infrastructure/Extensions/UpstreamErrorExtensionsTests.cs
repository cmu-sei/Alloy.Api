// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.Infrastructure.Extensions;

namespace Alloy.Api.Tests.Infrastructure.Extensions;

/// <summary>
/// A failed call to a sibling API becomes a message naming the service and what the user was doing,
/// rather than a 500 "A server error occurred."
/// </summary>
public class UpstreamErrorExtensionsTests
{
    private static Caster.Api.Client.ApiException CasterError(int statusCode) =>
        new("The HTTP status code of the response was not expected (" + statusCode + ").", statusCode, "",
            new Dictionary<string, IEnumerable<string>>(), null);

    /// <summary>
    /// Every status is a 502: a 404 or 403 from Caster passed on as Alloy's own would tell the client
    /// that the Alloy resource it asked for is missing or forbidden.
    /// </summary>
    [Theory]
    [InlineData(404, "It was not found in Caster.")]
    [InlineData(403, "You do not have permission to do this in Caster.")]
    [InlineData(401, "Caster did not accept your sign-in.")]
    [InlineData(503, "Caster reported an error (HTTP 503).")]
    [InlineData(409, "Caster rejected the request (HTTP 409).")]
    [InlineData(0, "Caster could not be reached.")]
    // nswag throws on a success whose body it cannot deserialize
    [InlineData(200, "Caster sent a response Alloy could not read.")]
    public void Upstream_status_codes_become_bad_gateways_naming_the_service(int statusCode, string reason)
    {
        var ex = Assert.IsType<UpstreamServiceException>(CasterError(statusCode).ToUserFacing("Caster", "load the Caster directories"));

        Assert.Equal($"Could not load the Caster directories. {reason}", ex.Message);
        Assert.Equal(HttpStatusCode.BadGateway, ex.GetStatusCode());
        Assert.IsType<Caster.Api.Client.ApiException>(ex.InnerException);
    }

    [Fact]
    public void An_unreachable_service_is_reported_as_unreachable()
    {
        var ex = new HttpRequestException("Connection refused").ToUserFacing("Player", "load your Player views");

        Assert.Equal("Could not load your Player views. Player could not be reached.", ex.Message);
    }

    [Fact]
    public void A_cancelled_request_is_not_an_upstream_failure()
    {
        Assert.False(new TaskCanceledException().IsUpstreamFailure(requestCancelled: true));
        // HttpClient reports its own timeout as a TaskCanceledException
        Assert.True(new TaskCanceledException().IsUpstreamFailure(requestCancelled: false));
        Assert.False(new InvalidOperationException().IsUpstreamFailure(requestCancelled: false));
    }

    /// <summary>The request path and the launch worker agree on what counts as a failed call.</summary>
    [Fact]
    public void A_broken_connection_is_an_upstream_failure_as_it_is_for_the_launch_worker()
    {
        var broken = new IOException("The response ended prematurely.");

        Assert.True(broken.IsUpstreamFailure(requestCancelled: false));
        Assert.True(broken.Classify("load the Caster directories").IsTransient);
    }
}
