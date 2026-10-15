// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Alloy.Api.Tests.Data;

/// <summary>
/// The <c>AddEndRequestedAt</c> migration (Alloy.Api.Migrations.PostgreSQL), run down and up on a clone of
/// the migrated template: historical end dates become end requests, and a rollback turns pending requests
/// back into the <c>Ending</c> state the previous release understood.
/// </summary>
public class EndRequestedMigrationTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    /// <summary>The migration before <c>AddEndRequestedAt</c>.</summary>
    private const string PreviousMigration = "20260903175326_AddEventErrorFields";

    /// <summary>The active and paused rows whose historical end date was an end the worker had not finished.</summary>
    private static readonly string[] PendingRows = ["active-pending", "paused-pending"];

    private static readonly DateTime HistoricalEnd = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// One row per case: the name, the <see cref="EventStatus"/> and <see cref="InternalEventStatus"/> it had
    /// under the previous release, and whether it holds resources.
    /// </summary>
    private const string HistoricalEvents = """
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
        """;

    [Fact]
    public void The_PostgreSQL_snapshot_matches_the_current_model()
    {
        Assert.False(Db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Upgrading_turns_historical_end_dates_into_end_requests()
    {
        await RollBackAndInsertHistoricalEvents();

        await Migrator().MigrateAsync(cancellationToken: Ct);

        var events = await UpgradedEvents();
        Assert.Equal(8, events.Count);
        Assert.All(events.Where(x => x.Key != "active"), x => Assert.Equal(HistoricalEnd, x.Value.EndRequestedAt));
        Assert.Null(events["active"].EndRequestedAt);
    }

    [Fact]
    public async Task Upgrading_keeps_an_end_date_only_on_events_that_completed()
    {
        await RollBackAndInsertHistoricalEvents();

        await Migrator().MigrateAsync(cancellationToken: Ct);

        var events = await UpgradedEvents();
        Assert.Equal(
            ["ended", "expired", "failed-clean"],
            events.Where(x => x.Value.EndDate == HistoricalEnd).Select(x => x.Key).Order());
        Assert.All(events.Where(x => x.Value.EndDate != HistoricalEnd), x => Assert.Null(x.Value.EndDate));
    }

    /// <summary>
    /// Rolled back, a pending end request becomes <c>Ending</c>/<c>EndQueued</c> so the previous release's
    /// worker tears it down, and a completion recorded after the upgrade survives.
    /// </summary>
    [Fact]
    public async Task Rolling_back_keeps_end_requests_actionable_and_completions_recorded()
    {
        await RollBackAndInsertHistoricalEvents();
        await Migrator().MigrateAsync(cancellationToken: Ct);
        var upgraded = await UpgradedEvents();
        await Db.Database.ExecuteSqlRawAsync("UPDATE events SET end_date = '2026-09-02T00:00:00Z' WHERE name = 'ended'", Ct);

        await Migrator().MigrateAsync(PreviousMigration, Ct);

        var rolledBack = (await ReadPreviousSchemaEvents()).ToDictionary(x => x.Name);
        Assert.Equal(8, rolledBack.Count);
        Assert.Equal(
            [(EventStatus.Ending, InternalEventStatus.EndQueued), (EventStatus.Ending, InternalEventStatus.EndQueued)],
            PendingRows.Select(x => (rolledBack[x].Status, rolledBack[x].InternalStatus)));
        Assert.All(
            rolledBack.Values.Where(x => !PendingRows.Contains(x.Name)),
            x => Assert.Equal((upgraded[x.Name].Status, upgraded[x.Name].InternalStatus), (x.Status, x.InternalStatus)));
        Assert.All(
            rolledBack.Values,
            x => Assert.Equal((upgraded[x.Name].WorkspaceId, upgraded[x.Name].ViewId, upgraded[x.Name].ScenarioId), (x.WorkspaceId, x.ViewId, x.ScenarioId)));
        Assert.Equal(HistoricalEnd.AddDays(1), rolledBack["ended"].EndDate);
        Assert.Null(rolledBack["active"].EndDate);
        Assert.All(
            rolledBack.Values.Where(x => x.Name is not "ended" and not "active"),
            x => Assert.Equal(HistoricalEnd, x.EndDate));
    }

    private IMigrator Migrator() => Db.GetService<IMigrator>();

    private async Task RollBackAndInsertHistoricalEvents()
    {
        await Migrator().MigrateAsync(PreviousMigration, Ct);
        await Db.Database.ExecuteSqlRawAsync(HistoricalEvents, Ct);
    }

    private async Task<Dictionary<string, EventEntity>> UpgradedEvents()
    {
        await using var context = NewContext();

        return await context.Events.AsNoTracking().ToDictionaryAsync(e => e.Name, Ct);
    }

    /// <summary>Raw SQL: after the rollback the model has a column the table no longer does.</summary>
    private async Task<List<PreviousSchemaEvent>> ReadPreviousSchemaEvents()
    {
        await using var connection = new NpgsqlConnection(Session.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT name, status, internal_status, end_date, workspace_id, view_id, scenario_id FROM events", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);

        List<PreviousSchemaEvent> rows = [];

        while (await reader.ReadAsync(Ct))
        {
            rows.Add(new PreviousSchemaEvent(
                reader.GetString(0),
                (EventStatus)reader.GetInt32(1),
                (InternalEventStatus)reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                reader.IsDBNull(4) ? null : reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.IsDBNull(6) ? null : reader.GetGuid(6)));
        }

        return rows;
    }

    private sealed record PreviousSchemaEvent(
        string Name,
        EventStatus Status,
        InternalEventStatus InternalStatus,
        DateTime? EndDate,
        Guid? WorkspaceId,
        Guid? ViewId,
        Guid? ScenarioId);
}
