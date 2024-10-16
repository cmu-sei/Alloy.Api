// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.
using System;
using Microsoft.EntityFrameworkCore;

namespace Alloy.Api.Data;

public class AlloyContextFactory : IDbContextFactory<AlloyContext>
{
    private readonly IDbContextFactory<AlloyContext> _pooledFactory;
    private readonly IServiceProvider _serviceProvider;
    public AlloyContextFactory(
        IDbContextFactory<AlloyContext> pooledFactory,
        IServiceProvider serviceProvider)
    {
        _pooledFactory = pooledFactory;
        _serviceProvider = serviceProvider;
    }
    public AlloyContext CreateDbContext()
    {
        var context = _pooledFactory.CreateDbContext();
        // Inject the current scope's ServiceProvider
        context.ServiceProvider = _serviceProvider;
        return context;
    }
}