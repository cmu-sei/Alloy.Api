// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// Mirrors Startup's AddAutoMapper: the Startup assembly, and the IgnoreNullSourceValues convention for
// properties whose source is the nullable form of the destination. IgnoreNullSourceValues is internal to
// Alloy.Api, which grants Alloy.Api.Tests its internals, so the real resolver is used.

using System;
using Alloy.Api.Infrastructure.Mappings;
using AutoMapper;
using AutoMapper.Internal;

namespace Alloy.Api.Tests.Support;

/// <summary>The application's real AutoMapper configuration, built without starting the application.</summary>
public static class TestMapper
{
    private static readonly Lazy<MapperConfiguration> LazyConfiguration = new(() =>
        new MapperConfiguration(cfg =>
        {
            cfg.AddMaps(typeof(Startup).Assembly);
            cfg.Internal().ForAllPropertyMaps(
                pm => pm.SourceType != null && Nullable.GetUnderlyingType(pm.SourceType) == pm.DestinationType,
                (pm, c) => c.MapFrom<object, object, object, object>(new IgnoreNullSourceValues(), pm.SourceMember.Name));
        }));

    /// <summary>The shared configuration, built once for the run.</summary>
    public static MapperConfiguration Configuration => LazyConfiguration.Value;

    /// <summary>A mapper over <see cref="Configuration"/>. Thread-safe; tests share one.</summary>
    public static IMapper Mapper => LazyMapper.Value;

    private static readonly Lazy<IMapper> LazyMapper = new(() => LazyConfiguration.Value.CreateMapper());
}
