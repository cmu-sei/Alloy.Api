// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.Services;
using Alloy.Api.Tests.Support;
using Alloy.Api.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Alloy.Api.Tests.Services;

/// <summary>EventService with a request's own context or cancellation, using real generated sibling clients.</summary>
/// <remarks>Over HTTP each request reads afresh; only a service test can retain the snapshot read before a worker update.</remarks>
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

    /// <summary>Cancellation during Player's user lookup is followed by a Player user creation and a transient failure.</summary>
    [Fact]
    public async Task EnlistUser_creates_a_Player_user_after_the_user_lookup_is_cancelled()
    {
        var template = TestData.EventTemplate();
        var evt = TestData.Event(template.Id);
        evt.ViewId = Guid.NewGuid();
        var user = TestData.User();
        await Seed(template, evt, user);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var host = RootHost;
        Player.Api.Client.CreateUserCommand createdUser = null;
        CancellationToken lookupToken = default;
        host.SiblingApis.Handle = async message =>
        {
            if (message.Method == HttpMethod.Get && message.RequestUri.AbsolutePath == $"/api/users/{user.Id}")
            {
                request.Cancel();
                lookupToken = host.SiblingApis.LastRequestToken;
                throw new OperationCanceledException(request.Token);
            }

            if (message.Method == HttpMethod.Post && message.RequestUri.AbsolutePath == "/api/users")
            {
                createdUser = await message.Content.ReadFromJsonAsync<Player.Api.Client.CreateUserCommand>(Ct);
                return FakeSiblingApis.Json(new { id = createdUser.Id, name = createdUser.Name }, HttpStatusCode.Created);
            }

            throw new InvalidOperationException($"Unexpected request: {message.Method} {message.RequestUri}");
        };
        using var scope = host.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IEventService>();

        var error = await Assert.ThrowsAsync<TransientFailureException>(() =>
            service.EnlistUserAsync(evt.Id, user.Id, user.Name, request.Token));

        Assert.True(request.IsCancellationRequested);
        Assert.True(lookupToken.IsCancellationRequested);
        Assert.False(host.SiblingApis.LastRequestToken.IsCancellationRequested);
        Assert.NotNull(createdUser);
        Assert.Equal((user.Id, user.Name), (createdUser.Id, createdUser.Name));
        Assert.Equal([$"GET /api/users/{user.Id}", "POST /api/users"], host.SiblingApis.Requests);
        Assert.Equal("Failed to add the user to the virtual environment. The service could not be reached; retrying.", error.Message);
        Assert.False(await ReadBack(db => db.EventMemberships.AnyAsync(x => x.UserId == user.Id, Ct)));
        Assert.False(await ReadBack(db => db.EventTemplateMemberships.AnyAsync(x => x.UserId == user.Id, Ct)));
    }

    /// <summary>Cancellation during Steamfitter's user lookup is followed by a Steamfitter user creation.</summary>
    [Fact]
    public async Task EnlistUser_creates_a_Steamfitter_user_after_the_user_lookup_is_cancelled()
    {
        var template = TestData.EventTemplate();
        var evt = TestData.Event(template.Id);
        evt.ScenarioId = Guid.NewGuid();
        var user = TestData.User();
        await Seed(template, evt, user);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var host = RootHost;
        Steamfitter.Api.Client.User createdUser = null;
        CancellationToken lookupToken = default;
        host.SiblingApis.Handle = async message =>
        {
            if (message.Method == HttpMethod.Get && message.RequestUri.AbsolutePath == $"/api/users/{user.Id}")
            {
                request.Cancel();
                lookupToken = host.SiblingApis.LastRequestToken;
                throw new OperationCanceledException(request.Token);
            }

            if (message.Method == HttpMethod.Post && message.RequestUri.AbsolutePath == "/api/users")
            {
                createdUser = await message.Content.ReadFromJsonAsync<Steamfitter.Api.Client.User>(Ct);
                return FakeSiblingApis.Json(createdUser, HttpStatusCode.Created);
            }

            throw new InvalidOperationException($"Unexpected request: {message.Method} {message.RequestUri}");
        };
        using var scope = host.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IEventService>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.EnlistUserAsync(evt.Id, user.Id, user.Name, request.Token));

        Assert.True(request.IsCancellationRequested);
        Assert.True(lookupToken.IsCancellationRequested);
        Assert.False(host.SiblingApis.LastRequestToken.IsCancellationRequested);
        Assert.NotNull(createdUser);
        Assert.Equal((user.Id, user.Name), (createdUser.Id, createdUser.Name));
        Assert.Equal([$"GET /api/users/{user.Id}", "POST /api/users"], host.SiblingApis.Requests);
        Assert.False(await ReadBack(db => db.EventMemberships.AnyAsync(x => x.UserId == user.Id, Ct)));
        Assert.False(await ReadBack(db => db.EventTemplateMemberships.AnyAsync(x => x.UserId == user.Id, Ct)));
    }

    /// <summary>Cancellation when enlistment starts saving still commits both memberships.</summary>
    [Fact]
    public async Task EnlistUser_saves_both_memberships_when_the_request_is_cancelled_at_the_save()
    {
        var template = TestData.EventTemplate();
        var evt = TestData.Event(template.Id);
        var user = TestData.User();
        await Seed(template, evt, user);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var scope = RootHost.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AlloyContext>();
        context.SavingChanges += (_, _) => request.Cancel();
        var service = scope.ServiceProvider.GetRequiredService<IEventService>();

        var enlisted = await service.EnlistUserAsync(evt.Id, user.Id, user.Name, request.Token);

        Assert.True(request.IsCancellationRequested);
        Assert.Equal(evt.Id, enlisted.Id);
        Assert.Equal(TestData.EventRoles.Member,
            await ReadBack(db => db.EventMemberships.Where(x => x.EventId == evt.Id && x.UserId == user.Id).Select(x => x.RoleId).SingleAsync(Ct)));
        Assert.Equal(TestData.EventTemplateRoles.Observer,
            await ReadBack(db => db.EventTemplateMemberships.Where(x => x.EventTemplateId == template.Id && x.UserId == user.Id).Select(x => x.RoleId).SingleAsync(Ct)));
        Assert.Empty(RootHost.SiblingApis.Requests);
    }

    private async Task<EventEntity> Stored(Guid id)
    {
        await using var context = NewContext();

        return await context.Events.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
    }
}
