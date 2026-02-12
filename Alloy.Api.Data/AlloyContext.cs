// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Data.Extensions;
using Alloy.Api.Data.Models;
using Crucible.Common.EntityEvents;
using Crucible.Common.EntityEvents.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Alloy.Api.Data
{
    [GenerateEntityEventInterfaces(typeof(INotification))]
    public class AlloyContext : EventPublishingDbContext
    {

        public AlloyContext(DbContextOptions<AlloyContext> options) : base(options) { }

        public DbSet<EventTemplateEntity> EventTemplates { get; set; }
        public DbSet<EventEntity> Events { get; set; }
        public DbSet<PermissionEntity> Permissions { get; set; }
        public DbSet<UserEntity> Users { get; set; }
        public DbSet<SystemRoleEntity> SystemRoles { get; set; }
        public DbSet<EventRoleEntity> EventRoles { get; set; }
        public DbSet<EventMembershipEntity> EventMemberships { get; set; }
        public DbSet<EventTemplateRoleEntity> EventTemplateRoles { get; set; }
        public DbSet<EventTemplateMembershipEntity> EventTemplateMemberships { get; set; }
        public DbSet<GroupEntity> Groups { get; set; }
        public DbSet<GroupMembershipEntity> GroupMemberships { get; set; }

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

        public override int SaveChanges()
        {
            UpdateBaseEntityFields();
            return base.SaveChanges();
        }

        public override async Task<int> SaveChangesAsync(CancellationToken ct = default(CancellationToken))
        {
            UpdateBaseEntityFields();
            return await base.SaveChangesAsync(ct);
        }

        private void UpdateBaseEntityFields()
        {
            var addedEntries = ChangeTracker.Entries().Where(x => x.State == EntityState.Added);
            var modifiedEntries = ChangeTracker.Entries().Where(x => x.State == EntityState.Modified);
            foreach (var entry in addedEntries)
            {
                // add info to entities that are base entities
                try
                {
                    ((BaseEntity)entry.Entity).DateCreated = DateTime.UtcNow;
                    ((BaseEntity)entry.Entity).DateModified = null;
                    ((BaseEntity)entry.Entity).ModifiedBy = null;
                }
                catch
                { }
            }
            foreach (var entry in modifiedEntries)
            {
                // add info to entities that are base entities
                try
                {
                    ((BaseEntity)entry.Entity).DateModified = DateTime.UtcNow;
                    ((BaseEntity)entry.Entity).CreatedBy = (Guid)entry.OriginalValues["CreatedBy"];
                    ((BaseEntity)entry.Entity).DateCreated = DateTime.SpecifyKind((DateTime)entry.OriginalValues["DateCreated"], DateTimeKind.Utc);
                }
                catch
                { }
            }
        }

        protected override async Task PublishEventsAsync(CancellationToken cancellationToken)
        {
            if (EntityEvents.Count > 0 && ServiceProvider is not null)
            {
                var mediator = ServiceProvider.GetRequiredService<IMediator>();
                foreach (var evt in EntityEvents.Cast<INotification>())
                {
                    await mediator.Publish(evt, cancellationToken);
                }
            }
        }
    }
}
