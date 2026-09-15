using System;
using System.Threading.Tasks;
using Alloy.Api.Data.Models;
using Alloy.Api.Infrastructure.Exceptions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alloy.Api.Tests;

/// <summary>
/// An EventTemplate whose Player View has no default team is rejected, because at launch time Alloy
/// would otherwise fall back to the first team in the View that does not look administrative.
/// </summary>
[Collection("Postgres")]
public class EventTemplateViewValidationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task CreateRejectsAViewWithNoDefaultTeam()
    {
        using var env = new TestEnvironment(postgres);
        using var db = env.Context();
        var player = StubPlayerService.WithoutDefaultTeam();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            env.EventTemplateService(db, player).CreateAsync(new ViewModels.EventTemplate
            {
                Name = "Template", DurationHours = 1, ViewId = Guid.NewGuid()
            }, default));

        Assert.Contains("no default team", ex.Message);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, ex.GetStatusCode());
        Assert.Empty(await db.EventTemplates.ToListAsync());
    }

    [Fact]
    public async Task CreateAcceptsAViewWithADefaultTeam()
    {
        using var env = new TestEnvironment(postgres);
        using var db = env.Context();
        var viewId = Guid.NewGuid();

        var created = await env.EventTemplateService(db, StubPlayerService.WithDefaultTeam())
            .CreateAsync(new ViewModels.EventTemplate
            {
                Name = "Template", DurationHours = 1, ViewId = viewId
            }, default);

        Assert.Equal(viewId, created.ViewId);
        Assert.Single(await db.EventTemplates.ToListAsync());
    }

    [Fact]
    public async Task CreateAcceptsATemplateWithNoViewWithoutCallingPlayer()
    {
        using var env = new TestEnvironment(postgres);
        using var db = env.Context();
        // OnGetView is left throwing, so a call would fail the test rather than pass silently.
        var player = new StubPlayerService();

        var created = await env.EventTemplateService(db, player).CreateAsync(new ViewModels.EventTemplate
        {
            Name = "Template", DurationHours = 1, ViewId = null
        }, default);

        Assert.Null(created.ViewId);
        Assert.Equal(0, player.GetViewCalls);
    }

    [Fact]
    public async Task CreateFailsClosedWhenTheViewCannotBeRetrieved()
    {
        using var env = new TestEnvironment(postgres);
        using var db = env.Context();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            env.EventTemplateService(db, StubPlayerService.Unavailable()).CreateAsync(new ViewModels.EventTemplate
            {
                Name = "Template", DurationHours = 1, ViewId = Guid.NewGuid()
            }, default));

        Assert.Contains("Could not verify", ex.Message);
        Assert.Empty(await db.EventTemplates.ToListAsync());
    }

    [Fact]
    public async Task UpdateRejectsAViewWithNoDefaultTeamAndLeavesTheTemplateUnchanged()
    {
        using var env = new TestEnvironment(postgres);
        using var db = env.Context();
        var existing = new EventTemplateEntity
        {
            Id = Guid.NewGuid(), Name = "Original", Description = "Original description", DurationHours = 1
        };
        db.EventTemplates.Add(existing);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            env.EventTemplateService(db, StubPlayerService.WithoutDefaultTeam()).UpdateAsync(existing.Id,
                new ViewModels.EventTemplate
                {
                    Id = existing.Id, Name = "Original", Description = "Changed description",
                    DurationHours = 1, ViewId = Guid.NewGuid()
                }, default));

        Assert.Contains("no default team", ex.Message);
        var saved = await db.EventTemplates.AsNoTracking().SingleAsync(x => x.Id == existing.Id);
        Assert.Equal("Original description", saved.Description);
        Assert.Null(saved.ViewId);
    }

    /// <summary>
    /// The decided behaviour: validation runs on every save, so a Template saved before this rule
    /// existed cannot be edited at all until its View is fixed - even for an unrelated field.
    /// </summary>
    [Fact]
    public async Task UpdateRejectsADescriptionOnlyEditOfATemplateThatAlreadyHadABadView()
    {
        using var env = new TestEnvironment(postgres);
        using var db = env.Context();
        var viewId = Guid.NewGuid();
        var existing = new EventTemplateEntity
        {
            Id = Guid.NewGuid(), Name = "Legacy", Description = "Original description",
            DurationHours = 1, ViewId = viewId
        };
        db.EventTemplates.Add(existing);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            env.EventTemplateService(db, StubPlayerService.WithoutDefaultTeam()).UpdateAsync(existing.Id,
                new ViewModels.EventTemplate
                {
                    Id = existing.Id, Name = "Legacy", Description = "Changed description",
                    DurationHours = 1, ViewId = viewId
                }, default));

        var saved = await db.EventTemplates.AsNoTracking().SingleAsync(x => x.Id == existing.Id);
        Assert.Equal("Original description", saved.Description);
    }

    [Fact]
    public async Task UpdateAcceptsAViewWithADefaultTeam()
    {
        using var env = new TestEnvironment(postgres);
        using var db = env.Context();
        var existing = new EventTemplateEntity
        {
            Id = Guid.NewGuid(), Name = "Original", Description = "Original description", DurationHours = 1
        };
        db.EventTemplates.Add(existing);
        await db.SaveChangesAsync();
        var viewId = Guid.NewGuid();

        var updated = await env.EventTemplateService(db, StubPlayerService.WithDefaultTeam())
            .UpdateAsync(existing.Id, new ViewModels.EventTemplate
            {
                Id = existing.Id, Name = "Original", Description = "Changed description",
                DurationHours = 1, ViewId = viewId
            }, default);

        Assert.Equal(viewId, updated.ViewId);
        var saved = await db.EventTemplates.AsNoTracking().SingleAsync(x => x.Id == existing.Id);
        Assert.Equal("Changed description", saved.Description);
        Assert.Equal(viewId, saved.ViewId);
    }
}
