// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Services;
using AutoMapper;
using FakeItEasy;
using TUnit.Core;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using Crucible.Common.Testing.Fixtures;

namespace Alloy.Api.Tests.Unit.Services;

[Category("Unit")]
public class EventServiceTests
{
    private static EventService BuildEventService(AlloyContext context, IMapper? mapper = null)
    {
        var resolvedMapper = mapper ?? A.Fake<IMapper>();
        return FakeBuilder.BuildMeA<EventService>(context, resolvedMapper);
    }

    [Test]
    public async Task GetAsync_WhenEventsExist_ReturnsAllEvents()
    {
        // Arrange
        var events = new List<EventEntity>
        {
            new() { Id = Guid.NewGuid(), Name = "Event1", Username = "user1", Status = EventStatus.Active, StatusDate = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), Name = "Event2", Username = "user2", Status = EventStatus.Ended, StatusDate = DateTime.UtcNow }
        };

        var context = TestDbContextFactory.Create<AlloyContext>();
        context.Events.AddRange(events);
        context.SaveChanges();

        var mapper = A.Fake<IMapper>();
        var expectedResult = new List<ViewModels.Event>
        {
            new() { Id = events[0].Id, Name = "Event1" },
            new() { Id = events[1].Id, Name = "Event2" }
        };
        A.CallTo(() => mapper.Map<IEnumerable<ViewModels.Event>>(A<object>._)).Returns(expectedResult);

        var sut = BuildEventService(context, mapper);

        // Act
        var result = await sut.GetAsync(CancellationToken.None);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count()).IsEqualTo(2);
    }

    [Test]
    public async Task GetEventTemplateEventsAsync_WithTemplateId_ReturnsOnlyMatchingEvents()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var events = new List<EventEntity>
        {
            new() { Id = Guid.NewGuid(), EventTemplateId = templateId, Name = "Match", Username = "u1", Status = EventStatus.Active, StatusDate = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), EventTemplateId = Guid.NewGuid(), Name = "NoMatch", Username = "u2", Status = EventStatus.Active, StatusDate = DateTime.UtcNow }
        };

        var context = TestDbContextFactory.Create<AlloyContext>();
        context.Events.AddRange(events);
        context.SaveChanges();

        var mapper = A.Fake<IMapper>();
        A.CallTo(() => mapper.Map<IEnumerable<ViewModels.Event>>(A<object>._))
            .ReturnsLazily((object src) =>
            {
                var list = src as List<EventEntity>;
                return list?.Select(e => new ViewModels.Event { Id = e.Id, Name = e.Name }) ?? [];
            });

        var sut = BuildEventService(context, mapper);

        // Act
        var result = await sut.GetEventTemplateEventsAsync(templateId, CancellationToken.None);

        // Assert
        A.CallTo(() => mapper.Map<IEnumerable<ViewModels.Event>>(A<List<EventEntity>>.That.Matches(l => l.Count == 1)))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task DeleteAsync_RemovesEvent_ReturnsTrue()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var eventEntity = new EventEntity
        {
            Id = eventId,
            Name = "ToDelete",
            Username = "user",
            Status = EventStatus.Active,
            StatusDate = DateTime.UtcNow
        };

        var context = TestDbContextFactory.Create<AlloyContext>();
        context.Events.Add(eventEntity);
        context.SaveChanges();

        var sut = BuildEventService(context);

        // Act
        var result = await sut.DeleteAsync(eventId, CancellationToken.None);

        // Assert
        await Assert.That(result).IsTrue();
        await Assert.That(context.Events.FirstOrDefault(e => e.Id == eventId)).IsNull();
    }
}
