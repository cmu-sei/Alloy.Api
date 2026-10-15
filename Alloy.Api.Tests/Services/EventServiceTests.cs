// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Services;
using Alloy.Api.Tests.Support;
using Alloy.Api.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Alloy.Api.Tests.Services;

/// <summary>
/// <see cref="EventService"/> where a request's own context matters: a request that read the event
/// before the worker wrote to it. Over HTTP each request reads afresh, so only a service test can hold the
/// stale snapshot. The edit route's gate is tested over HTTP in <c>EventControllerTests</c>.
/// </summary>
public class EventServiceTests(DatabaseFixture fixture) : ServiceTestBase(fixture)
{
    [Fact]
    public async Task Update_from_a_stale_read_cannot_erase_the_workers_resources_or_the_end_intent()
    {
        var template = TestData.EventTemplate();
        var entity = TestData.Event(template.Id);
        await Seed(template, entity);
        using var edit = RootHost.CreateScope();
        await edit.ServiceProvider.GetRequiredService<AlloyContext>().Events.SingleAsync(x => x.Id == entity.Id, Ct);
        await using (var worker = NewContext())
        {
            var current = await worker.Events.SingleAsync(x => x.Id == entity.Id, Ct);
            current.WorkspaceId = Guid.NewGuid();
            current.Status = EventStatus.Applying;
            await worker.SaveChangesAsync(Ct);
        }
        using (var end = RootHost.CreateScope())
        {
            await end.ServiceProvider.GetRequiredService<IEventService>().EndAsync(entity.Id, Ct);
        }
        var before = await Stored(entity.Id);

        var updated = await edit.ServiceProvider.GetRequiredService<IEventService>()
            .UpdateAsync(entity.Id, new UpdateEventRequest { Name = "Edited", Description = "Description" }, Ct);

        var after = await Stored(entity.Id);
        Assert.Equal(entity.Id, updated.Id);
        Assert.Equal("Edited", after.Name);
        Assert.Equal(before.WorkspaceId, after.WorkspaceId);
        Assert.Equal(before.EndRequestedAt, after.EndRequestedAt);
        Assert.Equal((EventStatus.Applying, before.InternalStatus), (after.Status, after.InternalStatus));
        Assert.Equal((before.CreatedBy, before.DateCreated), (after.CreatedBy, after.DateCreated));
    }

    private async Task<EventEntity> Stored(Guid id)
    {
        await using var context = NewContext();

        return await context.Events.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }
}
