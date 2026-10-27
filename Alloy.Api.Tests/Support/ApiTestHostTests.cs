// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Authorization;
using Alloy.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Alloy.Api.Tests.Support;

/// <summary>
/// <see cref="ApiTestHost"/> re-declares registrations <c>Startup</c> makes; constructing each service it
/// offers through it makes a missing registration fail one test by name rather than a service test far away.
/// </summary>
public class ApiTestHostTests(DatabaseFixture fixture) : ServiceTestBase(fixture)
{
    [Fact]
    public void The_worker_resolves()
    {
        Assert.NotNull(RootHost.Resolve<AlloyBackgroundService>());
    }

    [Fact]
    public void Every_scoped_service_resolves_in_a_scope()
    {
        using var scope = RootHost.CreateScope();

        Assert.All(
            new[]
            {
                typeof(AlloyContext), typeof(IEventService), typeof(IEventTemplateService), typeof(IPlayerService),
                typeof(ICasterService), typeof(ISteamfitterService), typeof(IAlloyAuthorizationService),
                typeof(IUserClaimsService)
            },
            type => Assert.NotNull(scope.ServiceProvider.GetRequiredService(type)));
    }

    [Fact]
    public void A_scope_has_a_context_of_its_own()
    {
        using var first = RootHost.CreateScope();
        using var second = RootHost.CreateScope();

        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<AlloyContext>(),
            second.ServiceProvider.GetRequiredService<AlloyContext>());
    }
}
