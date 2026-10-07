// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Alloy.Api.Tests.Support;

namespace Alloy.Api.Tests.Hubs;

/// <summary>
/// <c>EngineHub</c>'s own <c>[Authorize(AuthenticationSchemes = "Bearer")]</c>, over the in-process
/// server at <c>/hubs/engine</c>: the connection's negotiate request, which SignalR authorizes before any
/// hub method runs. The methods' own checks are <c>EngineHubTests</c>.
/// </summary>
public class EngineHubConnectionTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string Negotiate = "/hubs/engine/negotiate?negotiateVersion=1";

    [Fact]
    public async Task A_signed_in_user_may_negotiate_a_connection()
    {
        var actor = await Actor().SeedAsync();

        using var response = await Client(actor).PostAsync(Negotiate, null, Ct);

        await AssertStatus(HttpStatusCode.OK, response);
    }

    /// <summary>A request with no identity is answered with a 401; the anonymous client is the case under test.</summary>
    [Fact]
    public async Task A_connection_with_no_identity_is_unauthorized()
    {
        using var response = await Client().PostAsync(Negotiate, null, Ct);

        await AssertStatus(HttpStatusCode.Unauthorized, response);
    }
}
