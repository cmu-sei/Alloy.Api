// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// The run-wide factory, database gate step 1B (Program.Main runs InitializeDatabase with no switch), with
// the shared HubRecorder for the one hub and the shared StubHttpMessageHandler for every outbound call.

using System.Net.Http;
using Alloy.Api.Data;
using Alloy.Api.Hubs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Alloy.Api.Tests.Support;

/// <summary>
/// Hosts <c>Alloy.Api</c> in process over <c>TestServer</c>, so that tests drive the real application: the
/// real <c>Startup</c>, the real MVC filters, the real authorization stack and the real claims
/// transformer.
/// </summary>
/// <remarks>
/// <para>
/// One instance serves the whole run, declared in <c>AssemblyFixtures.cs</c>. Everything the application
/// registers as a singleton is therefore shared by every test, which is what
/// <see cref="TestConfiguration"/>'s claims-caching entry and <see cref="TestDatabaseScope"/> exist to
/// deal with. Only three things are not the application's own: token validation
/// (<see cref="TestAuthHandler"/>), the context registration (<see cref="TestDatabaseScope"/>), and the
/// collaborators that leave the process.
/// </para>
/// <para>
/// No Crucible API's Program matches the CreateHostBuilder convention HostFactoryResolver looks for, so
/// WebApplicationFactory runs <c>Program.Main</c>, and Main runs <c>InitializeDatabase</c>. Alloy has no
/// switch that skips it, so the host gets a throwaway clone of the migrated template
/// (<see cref="DatabaseFixture.HostDatabase"/>); migrating it is a no-op and the empty <c>SeedData</c>
/// adds nothing.
/// </para>
/// <para>
/// The singleton <c>IAlloyEventQueue</c> stays real. With the hosted services removed nothing takes from
/// it, so a launch, end or redeploy request only adds to it; the worker is tested by
/// <c>AlloyBackgroundServiceTests</c>, which drives one of its own.
/// </para>
/// </remarks>
public sealed class AlloyAppFactory : WebApplicationFactory<Program>, ITestHttpHost
{
    public AlloyAppFactory() => TestIdentity.Arrange(OutboundHttp);

    /// <summary>
    /// Answers every request the application makes over HTTP: the identity provider (fixed here), and
    /// Player, Caster and Steamfitter at the shipped <c>ClientSettings:urls</c>. Arrange a url of your
    /// own on it, keyed on an id the test minted.
    /// </summary>
    public StubHttpMessageHandler OutboundHttp { get; } = new();

    /// <summary>What the application broadcast through <c>EngineHub</c>, per group.</summary>
    public HubRecorder<EngineHub> Hub { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Production, so the developer exception page stays off and JsonExceptionFilter answers as it
        // does in a deployment (a 500's message in Detail, never the stack trace).
        builder.UseEnvironment("Production");

        // 1B. Main's InitializeDatabase needs a real database to migrate and seed.
        builder.UseSetting("Database:Provider", "PostgreSQL");
        builder.UseSetting("ConnectionStrings:PostgreSQL", DatabaseFixture.HostDatabase().ConnectionString);

        builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(TestConfiguration.Values));

        builder.ConfigureTestServices(services =>
        {
            // AlloyBackgroundService and AlloyQueryService start in the background and reach for
            // databases and services no test owns. AlloyBackgroundServiceTests drives the worker directly.
            services.RemoveAll<IHostedService>();

            // EngineHub names the Bearer scheme ([Authorize(AuthenticationSchemes = "Bearer")]), so the
            // test handler is registered under that name as well. Startup's JWT bearer registration claims
            // the name, and every registration that configures AuthenticationOptions is one
            // IConfigureOptions, so dropping them drops it (and Startup's default scheme) first.
            services.RemoveAll<IConfigureOptions<AuthenticationOptions>>();
            services
                .AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(JwtBearerDefaults.AuthenticationScheme, null);

            // InitializeDatabase resolves the context from a scope of its own, outside any request, where
            // the one-argument registration has no X-Test-Session header to route by and throws. Until the
            // host has started, such a resolution gets the host's own database, over that session's own
            // services, so the seed's entity events never reach the real handlers or the hub recorder;
            // afterwards it throws again, so a stray resolution outside a request still fails loudly.
            TestDatabaseScope.ReplaceRegistration<AlloyContext>(
                services, () => _started ? null : DatabaseFixture.HostDatabase());

            // SignalR registers hub contexts as an open generic, which RemoveAll of a closed type cannot
            // match; a later closed registration wins on resolution.
            services.AddSingleton<IHubContext<EngineHub>>(Hub);

            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(OutboundHttp));
        });
    }

    private readonly System.Threading.Lock _creating = new();
    private IHost _host;

    /// <summary>Set once the host has started, which is after <c>Program.Main</c>'s <c>InitializeDatabase</c>.</summary>
    private volatile bool _started;

    /// <summary>Builds the one host, under a lock, and hands it to every caller.</summary>
    /// <remarks>
    /// <c>WebApplicationFactory.StartServer</c> takes no lock, so two tests asking for their first client at
    /// once would each build a host (each running <c>Program.Main</c>), and the run would continue on two
    /// of them. Every way into the host (<c>CreateClient</c> in any overload, <c>Services</c>,
    /// <c>Server</c>) goes through <c>StartServer</c> to here, so this lock covers them all.
    /// <c>base.CreateHost</c> returns once the host has started, and the deferred host starts only after
    /// <c>Main</c> has run <c>InitializeDatabase</c>.
    /// </remarks>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        lock (_creating)
        {
            if (_host is null)
            {
                _host = base.CreateHost(builder);
                _started = true;
            }

            return _host;
        }
    }
}
