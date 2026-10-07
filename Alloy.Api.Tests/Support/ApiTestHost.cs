// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// Mirrors Startup.ConfigureServices for the services that are constructed with options a test chose: the
// launch and end worker (AlloyBackgroundService, which reads ClientOptions through IOptionsMonitor and
// resolves its context and the resource-owner options from scopes) and EventService. The sibling APIs are
// a FakeSiblingApis the test scripts. The context is registered per scope over the test's database, as
// the worker disposes the one it resolves.

using System;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Authorization;
using Alloy.Api.Infrastructure.Extensions;
using Alloy.Api.Infrastructure.Identity;
using Alloy.Api.Infrastructure.Options;
using Alloy.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Alloy.Api.Tests.Support;

/// <summary>The application's services without the web host, over one test's database, as one user.</summary>
public sealed class ApiTestHost : IDisposable
{
    private readonly ServiceProvider _services;

    private ApiTestHost(ServiceProvider services, ApiTestHostOptions options)
    {
        _services = services;
        Options = options;
    }

    /// <summary>The options the host was built with; the sibling APIs' script is on <c>SiblingApis</c>.</summary>
    public ApiTestHostOptions Options { get; }

    /// <summary>The scripted Player, Caster, Steamfitter and identity provider.</summary>
    public FakeSiblingApis SiblingApis => Options.SiblingApis;

    /// <summary>A singleton, or a scoped service resolved from the root (one instance for the host).</summary>
    public T Resolve<T>() where T : notnull => _services.GetRequiredService<T>();

    /// <summary>A scope of its own, as a request has: its own context and services.</summary>
    public IServiceScope CreateScope() => _services.CreateScope();

    public static ApiTestHost Create(
        ITestDatabaseSession<AlloyContext> session,
        ClaimsPrincipal user,
        Action<ApiTestHostOptions> configure = null)
    {
        var options = new ApiTestHostOptions();
        configure?.Invoke(options);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();

        // A context per scope over the test's database, publishing to the session's own Mediator. The
        // worker disposes the context it resolves, so a single instance would not survive its first pass.
        services.AddScoped(_ => session.CreateContext());
        services.AddSingleton(TestMapper.Mapper);

        // The Authorization header is what a request carries and the Player, Caster and Steamfitter
        // client registrations forward; the Caster and Steamfitter ones cannot be built without it.
        var httpContext = new DefaultHttpContext { User = user };
        httpContext.Request.Headers.Authorization = $"Bearer {ApiTestHostOptions.BearerToken}";
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = httpContext });
        services.AddScoped<IPrincipal>(p => p.GetRequiredService<IHttpContextAccessor>().HttpContext.User);

        services.AddSingleton(options.ClientOptions);
        services.AddSingleton<IOptionsMonitor<ClientOptions>>(new FixedOptionsMonitor<ClientOptions>(options.ClientOptions));
        services.AddSingleton(options.ResourceOwnerAuthorization);
        services.AddSingleton<IOptionsMonitor<ResourceOptions>>(new FixedOptionsMonitor<ResourceOptions>(options.Resource));
        services.AddSingleton(new ClaimsTransformationOptions());
        services.AddSingleton<IHttpClientFactory>(options.SiblingApis);

        services.AddAuthorization();
        services.AddSingleton<IAuthorizationHandler, SystemPermissionHandler>();
        services.AddSingleton<IAuthorizationHandler, EventPermissionHandler>();
        services.AddSingleton<IAuthorizationHandler, EventTemplatePermissionHandler>();
        services.AddSingleton<IAuthorizationHandler, GroupPermissionsHandler>();
        services.AddScoped<IIdentityResolver, IdentityResolver>();
        services.AddScoped<IAlloyAuthorizationService, AuthorizationService>();
        services.AddScoped<IUserClaimsService, UserClaimsService>();

        services.AddPlayerApiClient();
        services.AddCasterApiClient();
        services.AddSteamfitterApiClient();
        services.AddScoped<IPlayerService, PlayerService>();
        services.AddScoped<ICasterService, CasterService>();
        services.AddScoped<ISteamfitterService, SteamfitterService>();
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IEventTemplateService, EventTemplateService>();

        services.AddSingleton<IAlloyEventQueue, AlloyEventQueue>();
        services.AddSingleton<StartupHealthCheck>();
        services.AddSingleton<TelemetryService>();
        services.AddSingleton<AlloyBackgroundService>();

        return new ApiTestHost(services.BuildServiceProvider(), options);
    }

    public void Dispose()
    {
        _services.Dispose();
        Options.SiblingApis.Dispose();
    }

    private sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string name) => value;

        public IDisposable OnChange(Action<T, string> listener) => null;
    }
}

/// <summary>
/// The configuration-backed options an <see cref="ApiTestHost"/> exposes, defaulted so every service runs
/// and every wait ends within a test's budget.
/// </summary>
public sealed class ApiTestHostOptions
{
    /// <summary>The bearer token of the host's request, as a sibling API receives it.</summary>
    public const string BearerToken = "host-request-token";

    /// <summary>
    /// Two retries, one-minute Caster waits and no poll interval (0 seconds), so a test that waits for a
    /// status change waits only for the script. The urls are hosts no network resolves.
    /// </summary>
    public ClientOptions ClientOptions { get; } = new()
    {
        ApiClientLaunchFailureMaxRetries = 2,
        ApiClientEndFailureMaxRetries = 2,
        CasterPlanningMaxWaitMinutes = 1,
        CasterDeployMaxWaitMinutes = 1,
        CasterDestroyMaxWaitMinutes = 1,
        urls = new()
        {
            casterApi = "https://caster.test",
            playerApi = "https://player.test",
            steamfitterApi = "https://steamfitter.test"
        }
    };

    public ResourceOwnerAuthorizationOptions ResourceOwnerAuthorization { get; } = new()
    {
        Authority = $"https://{FakeSiblingApis.IdentityHost}",
        ClientId = "test",
        UserName = "test",
        Password = "test"
    };

    public ResourceOptions Resource { get; } = new() { MaxEventsForBasicUser = 2 };

    public FakeSiblingApis SiblingApis { get; } = new();
}
