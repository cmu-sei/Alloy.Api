// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Alloy.Api.Data.Models
{
    public class EventUserEntity : BaseEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid EventId { get; set; }

        public virtual EventEntity Event { get; set; }
    }

    public class EventInviteConfiguration : IEntityTypeConfiguration<EventUserEntity>
    {
        public void Configure(EntityTypeBuilder<EventUserEntity> builder)
        {
            builder.HasIndex(e => new { e.EventId, e.UserId }).IsUnique();
        }
    }
}