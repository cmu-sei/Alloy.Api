// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Alloy.Api.Tests.Support;

/// <summary>
/// Player, Caster and Steamfitter, answered by a script, and the identity provider the resource-owner
/// token comes from, answered by itself. For the launch and end worker and the Caster run helpers, which
/// the real generated clients drive through <see cref="IHttpClientFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// An app extra rather than the shared <see cref="StubHttpMessageHandler"/>, which answers each url from a
/// fixed table. The worker's subject is a conversation: a run reads as <c>Planning</c>, then
/// <c>Applying</c> once an apply was posted, a save-state conflict is followed by a terminal status, an
/// end request arrives while a poll is in flight. Those answers depend on what was asked before, so a
/// test scripts them in <see cref="Handle"/>.
/// </para>
/// <para>
/// One instance per test, owned by the test's <see cref="ApiTestHost"/> or created by the test itself;
/// never registered in the run-wide host.
/// </para>
/// </remarks>
public sealed class FakeSiblingApis : HttpMessageHandler, IHttpClientFactory
{
    /// <summary>The authority <see cref="ApiTestHostOptions"/> configures for the resource-owner token.</summary>
    public const string IdentityHost = "identity.test";

    private readonly ConcurrentQueue<string> _requests = new();

    private readonly ConcurrentQueue<string> _urls = new();

    /// <summary>Every request to a sibling API, as <c>"{METHOD} {path}"</c>, in order. Identity is not listed.</summary>
    public IReadOnlyList<string> Requests => [.. _requests];

    /// <summary>
    /// The same requests with the sibling they went to, as <c>"{METHOD} https://{host}{path}"</c> (no query),
    /// in order, for a test that asserts which API was asked.
    /// </summary>
    public IReadOnlyList<string> Urls => [.. _urls];

    /// <summary>
    /// The script. By default every request fails the test that made it, so a test lists only the calls it
    /// expects.
    /// </summary>
    public Func<HttpRequestMessage, Task<HttpResponseMessage>> Handle { get; set; } =
        request => throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");

    /// <summary>A client per call over this handler, which outlives it.</summary>
    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    /// <summary>A client with no name, as <c>IHttpClientFactory.CreateClient()</c> asks.</summary>
    public HttpClient CreateClient() => CreateClient(string.Empty);

    /// <summary>A JSON answer, written with web defaults as the sibling APIs write it.</summary>
    public static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Encoding.UTF8,
            "application/json")
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri;

        if (uri.Host == IdentityHost)
        {
            if (uri.AbsolutePath.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(new
                {
                    issuer = $"https://{IdentityHost}",
                    token_endpoint = $"https://{IdentityHost}/token",
                    jwks_uri = $"https://{IdentityHost}/keys"
                }));
            }

            if (uri.AbsolutePath == "/keys")
            {
                return Task.FromResult(Json(new { keys = Array.Empty<object>() }));
            }

            return Task.FromResult(Json(new { access_token = "test", token_type = "Bearer", expires_in = 3600 }));
        }

        _requests.Enqueue($"{request.Method} {uri.AbsolutePath}");
        _urls.Enqueue($"{request.Method} {uri.GetLeftPart(UriPartial.Path)}");

        return Handle(request);
    }
}
