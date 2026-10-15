// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// Alloy's migrations live in Alloy.Api.Migrations.PostgreSQL. Program.Main has no switch that skips
// InitializeDatabase, so the run-wide factory takes step 1B: the database is static and the host gets a
// throwaway clone of its own (HostDatabase).

using System;
using System.Threading.Tasks;
using Alloy.Api.Data;

namespace Alloy.Api.Tests.Support;

/// <summary>
/// Owns the PostgreSQL database for the whole test run: starts it on first use and hands out an isolated
/// session per test.
/// </summary>
/// <remarks>
/// PostgreSQL exercises production's actual database, including the <c>if (Database.IsNpgsql())</c>
/// branch of <c>AlloyContext.OnModelCreating</c> and the real migration history. A usable Docker daemon
/// is therefore required by every test that takes a database.
/// </remarks>
public sealed class DatabaseFixture : IAsyncLifetime, ITestDatabaseSessionSource<AlloyContext>
{
    private static readonly PostgresTestDatabase<AlloyContext> _database = new(new()
    {
        Name = "alloy",
        TestAssembly = "Alloy.Api.Tests",
        // Production computes this as {AssemblyName}.Migrations.{provider} in
        // DatabaseExtensions.UseConfiguredDatabase. Without it EF looks in the context's own assembly
        // and finds none.
        MigrationsAssembly = "Alloy.Api.Migrations.PostgreSQL",
        CreateContext = AlloyContextFactory.CreateContext,
        CreateServices = AlloyContextFactory.CreateServices
    });

    /// <summary>
    /// The database Program.Main's InitializeDatabase migrates and seeds. Lazy, and blocking only inside
    /// ConfigureWebHost, which runs when the first test uses the host, so tests that need no database
    /// still run without Docker. Never dropped: the container goes at the end of the run.
    /// </summary>
    private static readonly Lazy<Task<ITestDatabaseSession<AlloyContext>>> _host =
        new(() => _database.BeginSessionAsync());

    /// <summary>Nothing to do here: the container starts on the first request for a session.</summary>
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public Task<ITestDatabaseSession<AlloyContext>> BeginSessionAsync() => _database.BeginSessionAsync();

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    /// <summary>The host's own database, for the factory's step 1B.</summary>
    public static ITestDatabaseSession<AlloyContext> HostDatabase() => _host.Value.GetAwaiter().GetResult();
}
