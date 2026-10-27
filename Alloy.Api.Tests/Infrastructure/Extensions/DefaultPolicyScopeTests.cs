// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Alloy.Api.Tests.Infrastructure.Extensions;

/// <summary>
/// The scopes Alloy requires of every token, each of <c>Authorization:AuthorizationScope</c>
/// (<c>player player-vm alloy steamfitter caster</c>): the MVC-wide <c>AuthorizeFilter</c> that
/// <c>Startup.ConfigureServices</c> builds with <c>RequireScope</c>, in front of every controller, and the
/// default policy <c>AuthorizationPolicyExtension.AddAuthorizationPolicy</c> builds with
/// <c>RequireClaim("scope", ...)</c>, behind <c>BaseController</c>'s and <c>EngineHub</c>'s
/// <c>[Authorize]</c> and the Prometheus endpoint's <c>RequireAuthorization()</c>.
/// </summary>
/// <remarks>
/// Each caller holds what its route asks for, and <c>TestAuthHandler.ScopeHeader</c> replaces the scopes its
/// token carries for that one request, so a refusal is the scope requirement's alone. The allowed case of
/// each family sends every configured scope explicitly.
/// </remarks>
public class DefaultPolicyScopeTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)
{
    private const string PlayerScope = "player";

    private const string PlayerVmScope = "player-vm";

    private const string AlloyScope = "alloy";

    private const string SteamfitterScope = "steamfitter";

    private const string CasterScope = "caster";

    /// <summary>A controller route behind both copies of the scope requirement; the caller holds its ViewUsers.</summary>
    private const string Users = "api/users";

    private const string HubNegotiate = "/hubs/engine/negotiate?negotiateVersion=1";

    /// <summary>Where <c>MapPrometheusScrapingEndpoint()</c> serves by default.</summary>
    private const string Metrics = "/metrics";

    // Controllers

    [Fact]
    public async Task A_controller_route_with_every_configured_scope_is_allowed_for_a_caller_holding_its_permission()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();

        using var response = await Send(actor, HttpMethod.Get, Users, ConfiguredScopes());

        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task A_controller_route_is_forbidden_for_a_token_without_the_player_scope()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();

        using var response = await Send(actor, HttpMethod.Get, Users, WithoutScope(PlayerScope));

        await AssertStatus(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task A_controller_route_is_forbidden_for_a_token_without_the_player_vm_scope()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();

        using var response = await Send(actor, HttpMethod.Get, Users, WithoutScope(PlayerVmScope));

        await AssertStatus(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task A_controller_route_is_forbidden_for_a_token_without_the_alloy_scope()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();

        using var response = await Send(actor, HttpMethod.Get, Users, WithoutScope(AlloyScope));

        await AssertStatus(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task A_controller_route_is_forbidden_for_a_token_without_the_steamfitter_scope()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();

        using var response = await Send(actor, HttpMethod.Get, Users, WithoutScope(SteamfitterScope));

        await AssertStatus(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task A_controller_route_is_forbidden_for_a_token_without_the_caster_scope()
    {
        var actor = await Actor().WithSystemPermissions(SystemPermission.ViewUsers).SeedAsync();

        using var response = await Send(actor, HttpMethod.Get, Users, WithoutScope(CasterScope));

        await AssertStatus(HttpStatusCode.Forbidden, response);
    }

    // EngineHub

    [Fact]
    public async Task The_hub_negotiate_with_every_configured_scope_is_allowed()
    {
        var actor = await ViewEventsHolder();

        using var response = await Send(actor, HttpMethod.Post, HubNegotiate, ConfiguredScopes());

        await AssertStatus(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task The_hub_negotiate_is_forbidden_for_a_token_without_the_player_scope()
    {
        var actor = await ViewEventsHolder();

        using var response = await Send(actor, HttpMethod.Post, HubNegotiate, WithoutScope(PlayerScope));

        await AssertStatus(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task The_hub_negotiate_is_forbidden_for_a_token_without_the_player_vm_scope()
    {
        var actor = await ViewEventsHolder();

        using var response = await Send(actor, HttpMethod.Post, HubNegotiate, WithoutScope(PlayerVmScope));

        await AssertStatus(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task The_hub_negotiate_is_forbidden_for_a_token_without_the_alloy_scope()
    {
        var actor = await ViewEventsHolder();

        using var response = await Send(actor, HttpMethod.Post, HubNegotiate, WithoutScope(AlloyScope));

        await AssertStatus(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task The_hub_negotiate_is_forbidden_for_a_token_without_the_steamfitter_scope()
    {
        var actor = await ViewEventsHolder();

        using var response = await Send(actor, HttpMethod.Post, HubNegotiate, WithoutScope(SteamfitterScope));

        await AssertStatus(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task The_hub_negotiate_is_forbidden_for_a_token_without_the_caster_scope()
    {
        var actor = await ViewEventsHolder();

        using var response = await Send(actor, HttpMethod.Post, HubNegotiate, WithoutScope(CasterScope));

        await AssertStatus(HttpStatusCode.Forbidden, response);
    }

    // Prometheus scraping endpoint

    /// <summary>The scraping endpoint asks for no permission, only the default policy.</summary>
    [Fact]
    public async Task Metrics_with_every_configured_scope_are_served_to_an_actor()
    {
        var actor = await ViewEventsHolder();

        using var response = await Send(actor, HttpMethod.Get, Metrics, ConfiguredScopes());

        await AssertStatus(HttpStatusCode.OK, response);
    }

    /// <summary>A request with no identity is answered with a 401; the anonymous client is the case under test.</summary>
    [Fact]
    public async Task Metrics_without_an_identity_are_unauthorized()
    {
        using var response = await Client().GetAsync(Metrics, Ct);

        await AssertStatus(HttpStatusCode.Unauthorized, response);
    }

    [Fact]
    public async Task Metrics_are_forbidden_for_a_token_without_the_player_scope()
    {
        var actor = await ViewEventsHolder();

        using var response = await Send(actor, HttpMethod.Get, Metrics, WithoutScope(PlayerScope));

        await AssertStatus(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Metrics_are_forbidden_for_a_token_without_the_player_vm_scope()
    {
        var actor = await ViewEventsHolder();

        using var response = await Send(actor, HttpMethod.Get, Metrics, WithoutScope(PlayerVmScope));

        await AssertStatus(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Metrics_are_forbidden_for_a_token_without_the_alloy_scope()
    {
        var actor = await ViewEventsHolder();

        using var response = await Send(actor, HttpMethod.Get, Metrics, WithoutScope(AlloyScope));

        await AssertStatus(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Metrics_are_forbidden_for_a_token_without_the_steamfitter_scope()
    {
        var actor = await ViewEventsHolder();

        using var response = await Send(actor, HttpMethod.Get, Metrics, WithoutScope(SteamfitterScope));

        await AssertStatus(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Metrics_are_forbidden_for_a_token_without_the_caster_scope()
    {
        var actor = await ViewEventsHolder();

        using var response = await Send(actor, HttpMethod.Get, Metrics, WithoutScope(CasterScope));

        await AssertStatus(HttpStatusCode.Forbidden, response);
    }

    /// <summary>A caller holding a system permission, for the routes that ask for none beyond the policy.</summary>
    private Task<TestActor> ViewEventsHolder() => Actor().WithSystemPermissions(SystemPermission.ViewEvents).SeedAsync();

    private string ConfiguredScopes() =>
        Factory.Services.GetRequiredService<IConfiguration>()["Authorization:AuthorizationScope"];

    /// <summary>Every configured scope but <paramref name="scope"/>, as one space-separated value.</summary>
    private string WithoutScope(string scope) =>
        string.Join(' ', ConfiguredScopes().Split(' ').Where(x => x != scope));

    private async Task<HttpResponseMessage> Send(TestActor actor, HttpMethod method, string url, string scopes)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.ScopeHeader, scopes);

        return await Client(actor).SendAsync(request, Ct);
    }
}
