// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Alloy.Api.Data.Models;

public class EventMembershipEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public Guid EventId { get; set; }
    public virtual EventEntity Event { get; set; }

    public Guid? UserId { get; set; }
    public virtual UserEntity User { get; set; }

    public Guid? GroupId { get; set; }
    public virtual GroupEntity Group { get; set; }

    public Guid RoleId { get; set; } = EventRoleDefaults.EventMemberRoleId;
    public EventRoleEntity Role { get; set; }


    public EventMembershipEntity() { }

    public EventMembershipEntity(Guid eventId, Guid? userId, Guid? groupId)
    {
        EventId = eventId;
        UserId = userId;
        GroupId = groupId;
    }

    public class EventMembershipEntityConfiguration : IEntityTypeConfiguration<EventMembershipEntity>
    {
        public void Configure(EntityTypeBuilder<EventMembershipEntity> builder)
        {
            builder.HasIndex(e => new { e.EventId, e.UserId, e.GroupId }).IsUnique();

            builder.Property(x => x.RoleId).HasDefaultValue(EventRoleDefaults.EventMemberRoleId);

            builder
                .HasOne(x => x.Event)
                .WithMany(x => x.Memberships)
                .HasForeignKey(x => x.EventId);

            builder
                .HasOne(x => x.User)
                .WithMany(x => x.EventMemberships)
                .HasForeignKey(x => x.UserId)
                .HasPrincipalKey(x => x.Id);

            builder
                .HasOne(x => x.Group)
                .WithMany(x => x.EventMemberships)
                .HasForeignKey(x => x.GroupId)
                .HasPrincipalKey(x => x.Id);
        }
    }
}