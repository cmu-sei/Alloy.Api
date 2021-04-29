// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.EntityFrameworkCore;
using Alloy.Api.Data.Models;
using Alloy.Api.Data.Extensions;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alloy.Api.Data
{
  public class AlloyContext : DbContext
  {
    private DbContextOptions<AlloyContext> _options;

    public AlloyContext(DbContextOptions<AlloyContext> options) : base(options)
    {
      _options = options;
    }

    public DbSet<EventTemplateEntity> EventTemplates { get; set; }
    public DbSet<EventEntity> Events { get; set; }
    public DbSet<EventUserEntity> EventUsers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      modelBuilder.ApplyConfigurations();

      // Apply PostgreSQL specific options
      if (Database.IsNpgsql())
      {
        modelBuilder.AddPostgresUUIDGeneration();
        modelBuilder.UsePostgresCasing();
      }

    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default(CancellationToken))
    {
      var addedEntries = ChangeTracker.Entries().Where(x => x.State == EntityState.Added);
      var modifiedEntries = ChangeTracker.Entries().Where(x => x.State == EntityState.Modified);
      foreach (var entry in addedEntries)
      {
        ((BaseEntity)entry.Entity).DateCreated = DateTime.UtcNow;
        ((BaseEntity)entry.Entity).DateModified = null;
        ((BaseEntity)entry.Entity).ModifiedBy = null;
      }
      foreach (var entry in modifiedEntries)
      {
        ((BaseEntity)entry.Entity).DateModified = DateTime.UtcNow;
        ((BaseEntity)entry.Entity).CreatedBy = (Guid)entry.OriginalValues["CreatedBy"];
        ((BaseEntity)entry.Entity).DateCreated = (DateTime)entry.OriginalValues["DateCreated"];
      }
      return await base.SaveChangesAsync(ct);
    }
  }
}

