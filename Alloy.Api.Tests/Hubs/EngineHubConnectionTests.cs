// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Hubs;
using Alloy.Api.Tests.Support;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Alloy.Api.Tests.Hubs;

/// <summary>
/// <c>EngineHub</c>'s own <c>[Authorize(AuthenticationSchemes = "Bearer")]</c>, over the in-process server at
/// <c>/hubs/engine</c>, where <c>Startup.Configure</c> maps it: a real SignalR connection over WebSockets,
/// and the negotiate request SignalR authorizes before any hub method runs. The methods' own checks are
/// <c>EngineHubTests</c>.
/// </summary>
/// <remarks>
/// The connection goes over WebSockets because its upgrade request carries the test's
/// <c>X-Test-Session</c> header, so the hub's <c>AlloyContext</c> is the test's database. Under long polling
/// an invocation runs outside any request and <c>TestDatabaseScope</c> could not pick one.
/// </remarks>
public class EngineHubConnectionTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string HubPath = "/hubs/engine";

    private const string Negotiate = "/hubs/engine/negotiate?negotiateVersion=1";

    [Fact]
    public async Task A_member_of_an_event_connects_over_WebSockets_and_invokes_JoinEvent()
    {
        var evt = TestData.Event(null);
        await Seed(evt);
        var actor = await Actor().OnEvent(evt.Id, [EventPermission.ViewEvent]).SeedAsync();
        await using var connection = Connection(actor);

        await connection.StartAsync(Ct);
        await connection.InvokeAsync(nameof(EngineHub.JoinEvent), evt.Id, Ct);

        Assert.Equal(HubConnectionState.Connected, connection.State);
    }

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

    /// <summary>A WebSocket to the TestServer, carrying the headers every ApiTestBase client sends.</summary>
    private HubConnection Connection(TestActor actor)
    {
        var session = Client().DefaultRequestHeaders.GetValues(TestDatabaseScope.HeaderName).Single();

        return new HubConnectionBuilder()
            .WithUrl($"http://localhost{HubPath}", options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, ct) =>
                {
                    var client = Factory.Server.CreateWebSocketClient();
                    client.ConfigureRequest = request =>
                    {
                        request.Headers[TestAuthHandler.UserHeader] = actor.Id.ToString();
                        request.Headers[TestAuthHandler.NameHeader] = actor.Name;
                        request.Headers[TestDatabaseScope.HeaderName] = session;
                    };

                    return await client.ConnectAsync(context.Uri, ct);
                };
            })
            .Build();
    }
}
