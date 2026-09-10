using System;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Alloy.Api.Tests;

[Collection("Postgres")]
public class EndRequestedMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public void PostgreSqlSnapshotMatchesTheCurrentModel()
    {
        using var database = postgres.CreateDatabase();
        using var db = Context(database.ConnectionString);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task BackfillPreservesHistoricalDatesAndRecoversPendingIntent()
    {
        using var database = postgres.CreateDatabase();
        using var db = Context(database.ConnectionString);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260903175326_AddEventErrorFields");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO events (id, name, created_by, date_created, user_id, status, status_date,
                internal_status, failure_count, last_end_internal_status, last_end_status,
                last_launch_internal_status, last_launch_status, end_date, workspace_id, scenario_id)
            SELECT uuid_generate_v4(), n::text, '00000000-0000-0000-0000-000000000000', now(),
                '00000000-0000-0000-0000-000000000000', s, now(), 21, 0, 0, 0, 0, 0,
                CASE WHEN n = 7 THEN NULL ELSE '2026-09-01T00:00:00Z'::timestamptz END,
                CASE WHEN n IN (3, 5) THEN uuid_generate_v4() ELSE NULL END,
                CASE WHEN n = 4 THEN uuid_generate_v4() ELSE NULL END
            FROM (VALUES (1,4), (2,5), (3,2), (4,11), (5,10), (6,10), (7,11)) AS samples(n,s);
            """);
        await migrator.MigrateAsync();

        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        using (var command = new NpgsqlCommand("SELECT name, end_date, end_requested_at FROM events ORDER BY name", connection))
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var sample = int.Parse(reader.GetString(0));
                Assert.Equal(sample is 3 or 4 or 5 or 7, reader.IsDBNull(1));
                Assert.Equal(sample == 7, reader.IsDBNull(2));
                if (sample != 7)
                    Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), reader.GetDateTime(2));
            }
        }

        await migrator.MigrateAsync("20260903175326_AddEventErrorFields");
        using var count = new NpgsqlCommand("SELECT COUNT(*) FROM events WHERE end_date IS NOT NULL", connection);
        Assert.Equal(6L, await count.ExecuteScalarAsync());
        using var column = new NpgsqlCommand("""
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_name = 'events' AND column_name = 'end_requested_at'
            """, connection);
        Assert.Equal(0L, await column.ExecuteScalarAsync());
    }

    private static AlloyContext Context(string connectionString) => new(new DbContextOptionsBuilder<AlloyContext>()
        .UseNpgsql(connectionString, x => x.MigrationsAssembly("Alloy.Api.Migrations.PostgreSQL")).Options);
}
