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
    public async Task MigrationPreservesHistoricalDatesAndRollbackKeepsEndRequestsActionable()
    {
        using var database = postgres.CreateDatabase();
        using var db = Context(database.ConnectionString);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260903175326_AddEventErrorFields");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO events (id, name, created_by, date_created, user_id, status, status_date,
                internal_status, failure_count, last_end_internal_status, last_end_status,
                last_launch_internal_status, last_launch_status, end_date, workspace_id, view_id, scenario_id)
            SELECT uuid_generate_v4(), n, '00000000-0000-0000-0000-000000000000', now(),
                '00000000-0000-0000-0000-000000000000', s, now(), i, 0, 0, 0, 0, 0,
                CASE WHEN n = 'active' THEN NULL ELSE '2026-09-01T00:00:00Z'::timestamptz END,
                uuid_generate_v4(), uuid_generate_v4(), uuid_generate_v4()
            FROM (VALUES ('ended',4,34), ('expired',5,34), ('failed-clean',10,9), ('failed-pending',10,32),
                ('active-pending',2,12), ('paused-pending',3,12), ('ending',11,33), ('active',2,12)) AS samples(n,s,i);
            UPDATE events SET workspace_id = NULL, view_id = NULL, scenario_id = NULL
            WHERE name IN ('ended', 'expired', 'failed-clean');
            """);
        await migrator.MigrateAsync();

        var date = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var events = await db.Events.AsNoTracking().ToDictionaryAsync(e => e.Name);
        Assert.Equal(8, events.Count);
        foreach (var (name, entity) in events)
        {
            Assert.Equal(name == "active" ? (DateTime?)null : date, entity.EndRequestedAt);
            Assert.Equal(name is "ended" or "expired" or "failed-clean" ? date : (DateTime?)null, entity.EndDate);
        }

        // A completion recorded after upgrade must survive rollback too.
        await db.Database.ExecuteSqlRawAsync("UPDATE events SET end_date = '2026-09-02T00:00:00Z' WHERE name = 'ended'");
        await migrator.MigrateAsync("20260903175326_AddEventErrorFields");
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        using var command = new NpgsqlCommand(
            "SELECT name, status, internal_status, end_date, workspace_id, view_id, scenario_id FROM events", connection);
        using var reader = await command.ExecuteReaderAsync();
        var count = 0;
        while (await reader.ReadAsync())
        {
            count++;
            var name = reader.GetString(0);
            var previous = events[name];
            var pending = name is "active-pending" or "paused-pending";
            Assert.Equal(pending ? EventStatus.Ending : previous.Status, (EventStatus)reader.GetInt32(1));
            Assert.Equal(pending ? InternalEventStatus.EndQueued : previous.InternalStatus, (InternalEventStatus)reader.GetInt32(2));
            Assert.Equal(name == "active" ? (DateTime?)null : name == "ended" ? date.AddDays(1) : date,
                reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3));
            Assert.Equal(previous.WorkspaceId, reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4));
            Assert.Equal(previous.ViewId, reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5));
            Assert.Equal(previous.ScenarioId, reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6));
        }
        Assert.Equal(8, count);
    }

    private static AlloyContext Context(string connectionString) => new(new DbContextOptionsBuilder<AlloyContext>()
        .UseNpgsql(connectionString, x => x.MigrationsAssembly("Alloy.Api.Migrations.PostgreSQL")).Options);
}
