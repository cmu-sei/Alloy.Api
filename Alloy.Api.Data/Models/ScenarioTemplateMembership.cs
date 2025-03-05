// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Alloy.Api.Data.Models;

public class EventTemplateMembershipEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public Guid EventTemplateId { get; set; }
    public virtual EventTemplateEntity EventTemplate { get; set; }

    public Guid? UserId { get; set; }
    public virtual UserEntity User { get; set; }

    public Guid? GroupId { get; set; }
    public virtual GroupEntity Group { get; set; }

    public Guid RoleId { get; set; } = EventTemplateRoleEntityDefaults.EventTemplateMemberRoleId;
    public EventTemplateRoleEntity Role { get; set; }


    public EventTemplateMembershipEntity() { }

    public EventTemplateMembershipEntity(Guid eventTemplateId, Guid? userId, Guid? groupId)
    {
        EventTemplateId = eventTemplateId;
        UserId = userId;
        GroupId = groupId;
    }

    public class EventTemplateMembershipConfiguration : IEntityTypeConfiguration<EventTemplateMembershipEntity>
    {
        public void Configure(EntityTypeBuilder<EventTemplateMembershipEntity> builder)
        {
            builder.HasIndex(e => new { e.EventTemplateId, e.UserId, e.GroupId }).IsUnique();

            builder.Property(x => x.RoleId).HasDefaultValue(EventTemplateRoleEntityDefaults.EventTemplateMemberRoleId);

            builder
                .HasOne(x => x.EventTemplate)
                .WithMany(x => x.Memberships)
                .HasForeignKey(x => x.EventTemplateId);

            builder
                .HasOne(x => x.User)
                .WithMany(x => x.EventTemplateMemberships)
                .HasForeignKey(x => x.UserId)
                .HasPrincipalKey(x => x.Id);

            builder
                .HasOne(x => x.Group)
                .WithMany(x => x.EventTemplateMemberships)
                .HasForeignKey(x => x.GroupId)
                .HasPrincipalKey(x => x.Id);
        }
    }
}