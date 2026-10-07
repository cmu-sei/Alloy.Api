// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// Adds to the template: AlloyJson, the options Alloy writes responses with, and a ReadAsync that reads
// with them; and the per-actor bearer token Alloy forwards to the sibling APIs.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.JsonConverters;
using Microsoft.AspNetCore.Mvc;

namespace Alloy.Api.Tests.Support;

/// <summary>
/// Base class for tests that drive the application over HTTP: the real routes, the real MVC filters, the
/// real claims transformer, the real authorization service, over a database no other test can see.
/// </summary>
/// <remarks>
/// Derived classes forward both fixtures:
/// <c>MyTests(DatabaseFixture fixture, AlloyAppFactory factory) : ApiTestBase(fixture, factory)</c>.
/// </remarks>
public abstract class ApiTestBase(DatabaseFixture fixture, AlloyAppFactory factory)
    : ApiTestBase<AlloyContext>(fixture, factory)
{
    protected DatabaseFixture Fixture { get; } = fixture;

    protected AlloyAppFactory Factory { get; } = factory;

    /// <summary>
    /// An actor holding every system permission, for the tests that are about what an endpoint does
    /// rather than who may call it. Seeded before each test.
    /// </summary>
    protected TestActor Root { get; private set; } = null!;

    /// <summary>A client that acts as <see cref="Root"/>.</summary>
    protected HttpClient RootClient => Client(Root);

    /// <summary>Starts describing an actor to seed: <c>await Actor().WithSystemPermissions(...).SeedAsync()</c>.</summary>
    protected TestActorBuilder Actor() => new(Db, Ct);

    /// <summary>
    /// A client that acts as <paramref name="actor"/>, cached per actor. It also carries
    /// <see cref="BearerToken"/> as its <c>Authorization</c> header, which <c>TestAuthHandler</c> ignores:
    /// a production request always has one, and the Player, Caster and Steamfitter clients
    /// (<c>ServiceCollectionExtensions.Add*ApiClient</c>) forward it, the Caster and Steamfitter ones
    /// failing to build without it.
    /// </summary>
    protected HttpClient Client(TestActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var client = ClientFor(actor.Id, actor.Name);
        client.DefaultRequestHeaders.Authorization ??= new AuthenticationHeaderValue("Bearer", BearerToken(actor));

        return client;
    }

    /// <summary>The token <paramref name="actor"/>'s requests carry, as the sibling APIs receive it.</summary>
    protected static string BearerToken(TestActor actor) => $"token-of-{actor.Id}";

    /// <summary>
    /// The options Startup's <c>AddJsonOptions</c> writes responses with: the shared
    /// <see cref="TestJson.Options"/> plus Alloy's <c>JsonNullableGuidConverter</c> and
    /// <c>JsonDateTimeConverter</c>.
    /// </summary>
    protected static readonly JsonSerializerOptions AlloyJson = new(TestJson.Options)
    {
        Converters = { new JsonNullableGuidConverter(), new JsonDateTimeConverter() }
    };

    /// <summary>
    /// Asserts <paramref name="response"/> succeeded and returns its body, read with <see cref="AlloyJson"/>
    /// through the shared options overload. Hides the one-argument shared <c>ReadAsync</c>, which reads with
    /// <see cref="TestJson.Options"/> and so fails on the <c>""</c> Alloy writes for a null <c>Guid?</c>.
    /// </summary>
    protected static new Task<TValue> ReadAsync<TValue>(HttpResponseMessage response) => ReadAsync<TValue>(response, AlloyJson);

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();

        Root = await Actor().WithName("Root").WithAllSystemPermissions().SeedAsync();
    }
}
