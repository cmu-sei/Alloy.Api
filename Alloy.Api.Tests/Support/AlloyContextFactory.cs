// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// AlloyContext takes only its options; production attaches no interceptor besides EntityEventInterceptor.

using System;
using Alloy.Api.Data;
using Crucible.Common.EntityEvents.Interceptors;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alloy.Api.Tests.Support;

/// <summary>Builds <see cref="AlloyContext"/> instances wired the way production wires them.</summary>
/// <remarks>
/// <see cref="AlloyContext"/> extends <c>EventPublishingDbContext</c>, and its <c>PublishEventsAsync</c>
/// resolves <see cref="IMediator"/> and a logger off the settable <c>ServiceProvider</c> property with
/// <c>GetRequiredService</c>. Both must be registered or the first event-publishing save throws.
/// </remarks>
internal static class AlloyContextFactory
{
    /// <summary>
    /// The provider a session shares across its contexts, and the substituted mediator tests assert on.
    /// A substitute is right here: each session gets its own, and only its own test reads it.
    /// </summary>
    public static (IServiceProvider Services, IMediator Mediator) CreateServices()
    {
        var mediator = Substitute.For<IMediator>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(mediator);

        return (services.BuildServiceProvider(), mediator);
    }

    /// <summary>
    /// A context over the given provider configuration, with the entity event interceptor attached so
    /// SaveChanges publishes events exactly as it does in production.
    /// </summary>
    public static AlloyContext CreateContext(
        Action<DbContextOptionsBuilder<AlloyContext>> configureProvider,
        IServiceProvider services)
    {
        var builder = new DbContextOptionsBuilder<AlloyContext>();
        configureProvider(builder);
        builder.AddInterceptors(new EntityEventInterceptor(NullLogger<EntityEventInterceptor>.Instance));

        return new AlloyContext(builder.Options) { ServiceProvider = services };
    }
}
