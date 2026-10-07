// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Alloy.Api.Tests.Support;

/// <summary>
/// The identity provider Alloy asks for its resource-owner token before it calls Player, Caster or
/// Steamfitter on its own behalf (<c>ApiClientsExtensions.RequestTokenAsync</c>).
/// </summary>
/// <remarks>
/// The addresses are the shipped <c>ResourceOwnerAuthorization:Authority</c>. The discovery document
/// passes IdentityModel's default policy (issuer equal to the authority, endpoints under it), which the
/// shipped <c>ValidateDiscoveryDocument: true</c> applies. The answers are the same for every test, so
/// fixing them in the run-wide <c>OutboundHttp</c> at construction shares nothing a test could change.
/// </remarks>
public static class TestIdentity
{
    public const string Authority = "http://localhost:5000";

    public const string DiscoveryUrl = Authority + "/.well-known/openid-configuration";

    public const string KeysUrl = Authority + "/keys";

    public const string TokenUrl = Authority + "/connect/token";

    public const string AccessToken = "test-access-token";

    public static void Arrange(StubHttpMessageHandler handler)
    {
        handler.RespondJson(DiscoveryUrl, new
        {
            issuer = Authority,
            token_endpoint = TokenUrl,
            jwks_uri = KeysUrl
        });
        handler.RespondJson(KeysUrl, new { keys = new object[0] });
        handler.RespondJson(TokenUrl, new
        {
            access_token = AccessToken,
            token_type = "Bearer",
            expires_in = 3600
        });
    }
}
