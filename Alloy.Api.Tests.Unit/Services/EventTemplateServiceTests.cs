// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using Alloy.Api.Data;
using Alloy.Api.Data.Models;
using Alloy.Api.Services;
using AutoMapper;
using FakeItEasy;
using Shouldly;
using Xunit;
using Crucible.Common.Testing.Fixtures;

namespace Alloy.Api.Tests.Unit.Services;

public class EventTemplateServiceTests
{
    private static EventTemplateService BuildService(AlloyContext context, IMapper? mapper = null)
    {
        var resolvedMapper = mapper ?? A.Fake<IMapper>();
        return FakeBuilder.BuildMeA<EventTemplateService>(context, resolvedMapper);
    }

    [Fact]
    public async Task GetAsync_ReturnsAllEventTemplates()
    {
        // Arrange
        var templates = new List<EventTemplateEntity>
        {
            new() { Id = Guid.NewGuid(), Name = "Template1", DurationHours = 2 },
            new() { Id = Guid.NewGuid(), Name = "Template2", DurationHours = 4 }
        };

        var context = TestDbContextFactory.Create<AlloyContext>();
        context.EventTemplates.AddRange(templates);
        context.SaveChanges();

        var mapper = A.Fake<IMapper>();
        var expected = new List<ViewModels.EventTemplate>
        {
            new() { Id = templates[0].Id, Name = "Template1" },
            new() { Id = templates[1].Id, Name = "Template2" }
        };
        A.CallTo(() => mapper.Map<IEnumerable<ViewModels.EventTemplate>>(A<object>._)).Returns(expected);

        var sut = BuildService(context, mapper);

        // Act
        var result = await sut.GetAsync(CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Count().ShouldBe(2);
    }

    [Fact]
    public async Task GetPublishedAsync_ReturnsOnlyPublishedTemplates()
    {
        // Arrange
        var templates = new List<EventTemplateEntity>
        {
            new() { Id = Guid.NewGuid(), Name = "Published", IsPublished = true, DurationHours = 2 },
            new() { Id = Guid.NewGuid(), Name = "Unpublished", IsPublished = false, DurationHours = 4 }
        };

        var context = TestDbContextFactory.Create<AlloyContext>();
        context.EventTemplates.AddRange(templates);
        context.SaveChanges();

        var mapper = A.Fake<IMapper>();
        A.CallTo(() => mapper.Map<IEnumerable<ViewModels.EventTemplate>>(A<object>._))
            .ReturnsLazily((object src) =>
            {
                var list = src as List<EventTemplateEntity>;
                return list?.Select(e => new ViewModels.EventTemplate { Id = e.Id, Name = e.Name }) ?? [];
            });

        var sut = BuildService(context, mapper);

        // Act
        var result = await sut.GetPublishedAsync(CancellationToken.None);

        // Assert
        A.CallTo(() => mapper.Map<IEnumerable<ViewModels.EventTemplate>>(
            A<List<EventTemplateEntity>>.That.Matches(l => l.Count == 1 && l[0].IsPublished)))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task DeleteAsync_RemovesTemplate_ReturnsTrue()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var template = new EventTemplateEntity
        {
            Id = templateId,
            Name = "ToDelete",
            DurationHours = 2
        };

        var context = TestDbContextFactory.Create<AlloyContext>();
        context.EventTemplates.Add(template);
        context.SaveChanges();

        var sut = BuildService(context);

        // Act
        var result = await sut.DeleteAsync(templateId, CancellationToken.None);

        // Assert
        result.ShouldBeTrue();
        context.EventTemplates.FirstOrDefault(t => t.Id == templateId).ShouldBeNull();
    }
}
