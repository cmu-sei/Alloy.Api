using System;
using System.Threading.Tasks;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Alloy.Api.Tests;

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }

public sealed class PostgresFixture : IAsyncLifetime
{
    // Match the development AppHost's PostgreSQL version. Each test gets its own database.
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17.6").Build();

    public Task InitializeAsync() => container.StartAsync();
    public async Task DisposeAsync() => await container.DisposeAsync();
    public TestDatabase CreateDatabase() => new(container.GetConnectionString());
}

public sealed class TestDatabase : IDisposable
{
    private readonly string adminConnectionString;
    private readonly string name = $"alloy_test_{Guid.NewGuid():N}";
    public string ConnectionString { get; }

    internal TestDatabase(string adminConnectionString)
    {
        this.adminConnectionString = adminConnectionString;
        using var connection = new NpgsqlConnection(adminConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        command.ExecuteNonQuery();
        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = name, Pooling = false
        }.ConnectionString;
    }

    public void Dispose()
    {
        using var connection = new NpgsqlConnection(adminConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", connection);
        command.ExecuteNonQuery();
    }
}
