// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Alloy.Api.Data.Models;

public class GroupMembershipEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public Guid GroupId { get; set; }
    public virtual GroupEntity Group { get; set; }

    public Guid UserId { get; set; }
    public virtual UserEntity User { get; set; }

    public GroupMembershipEntity() { }

    public GroupMembershipEntity(Guid groupId, Guid userId)
    {
        GroupId = groupId;
        UserId = userId;
    }

    public class GroupMembershipEntityConfiguration : IEntityTypeConfiguration<GroupMembershipEntity>
    {
        public void Configure(EntityTypeBuilder<GroupMembershipEntity> builder)
        {
            builder.HasIndex(e => new { e.GroupId, e.UserId }).IsUnique();

            builder
                .HasOne(tu => tu.Group)
                .WithMany(t => t.Memberships)
                .HasForeignKey(tu => tu.GroupId);

            builder
                .HasOne(tu => tu.User)
                .WithMany(u => u.GroupMemberships)
                .HasForeignKey(tu => tu.UserId)
                .HasPrincipalKey(u => u.Id);
        }
    }
}