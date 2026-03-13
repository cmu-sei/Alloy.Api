// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Alloy.Api.Data;
using Alloy.Api.Infrastructure.Authorization;
using Crucible.Common.Testing.Auth;
using Crucible.Common.Testing.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Alloy.Api.Tests.Integration.Fixtures;

public class AlloyTestContext : WebApplicationFactory<Program>, IAsyncInitializer, IAsyncDisposable
{
    private PostgreSqlContainer? _container;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder
            .UseEnvironment("Test")
            .ConfigureServices(services =>
            {
                if (_container is null)
                {
                    throw new InvalidOperationException(
                        "Cannot configure the test context: the database container has not been initialized.");
                }

                // Remove the existing DbContext registrations (pooled factory + scoped)
                services.RemoveAll<IDbContextFactory<AlloyContext>>();
                services.RemoveAll<DbContextOptions<AlloyContext>>();
                services.RemoveAll<AlloyContext>();

                // Remove the pooled factory options
                var descriptorsToRemove = services
                    .Where(d => d.ServiceType.FullName?.Contains("DbContextPool") == true
                             || d.ServiceType.FullName?.Contains("DbContextFactory") == true)
                    .ToList();
                foreach (var descriptor in descriptorsToRemove)
                {
                    services.Remove(descriptor);
                }

                // Add test database context
                services.AddDbContext<AlloyContext>(opts =>
                    opts.UseNpgsql(_container.GetConnectionString()));

                // Replace authentication with test handler
                services.AddAuthentication(TestAuthenticationHandler.AuthenticationSchemeName)
                    .AddScheme<TestAuthenticationHandlerOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.AuthenticationSchemeName, _ => { });

                // Replace claims transformation to pass through
                services.ReplaceService<IClaimsTransformation, TestClaimsTransformation>(allowMultipleReplace: true);

                // Replace authorization service to allow all
                services.ReplaceService<IAuthorizationService, TestAuthorizationService>();

                // Replace the custom Alloy authorization service with a permissive implementation
                services.RemoveService<IAlloyAuthorizationService>();
                services.AddScoped<IAlloyAuthorizationService, TestAlloyAuthorizationService>();

                // Remove hosted services that depend on external services
                services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
            });
    }

    public AlloyContext GetDbContext()
    {
        return Services.GetRequiredService<AlloyContext>();
    }

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithHostname("localhost")
            .WithUsername("foundry")
            .WithPassword("foundry")
            .WithImage("postgres:latest")
            .WithAutoRemove(true)
            .WithCleanUp(true)
            .Build();

        await _container.StartAsync();
    }

    public new async ValueTask DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
        await base.DisposeAsync();
    }
}
