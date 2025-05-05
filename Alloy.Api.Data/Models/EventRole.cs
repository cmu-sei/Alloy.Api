// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Alloy.Api.Data.Models;

public class EventRoleEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public bool AllPermissions { get; set; }

    public List<EventPermission> Permissions { get; set; }
}

public static class EventRoleDefaults
{
    public static Guid EventCreatorRoleId = new("1a3f26cd-9d99-4b98-b914-12931e786198");
    public static Guid EventReadOnlyRoleId = new("39aa296e-05ba-4fb0-8d74-c92cf3354c6f");
    public static Guid EventMemberRoleId = new("f870d8ee-7332-4f7f-8ee0-63bd07cfd7e4");
}

public class EventRoleConfiguration : IEntityTypeConfiguration<EventRoleEntity>
{
    public void Configure(EntityTypeBuilder<EventRoleEntity> builder)
    {
        builder.HasData(
            new EventRoleEntity
            {
                Id = EventRoleDefaults.EventCreatorRoleId,
                Name = "Manager",
                AllPermissions = true,
                Permissions = [],
                Description = "Can perform all actions on the Event"
            },
            new EventRoleEntity
            {
                Id = EventRoleDefaults.EventReadOnlyRoleId,
                Name = "Observer",
                AllPermissions = false,
                Permissions = [EventPermission.ViewEvent],
                Description = "Has read only access to the Event"
            },
            new EventRoleEntity
            {
                Id = EventRoleDefaults.EventMemberRoleId,
                Name = "Member",
                AllPermissions = false,
                Permissions = [
                    EventPermission.ViewEvent,
                    EventPermission.EditEvent
                ],
                Description = "Has read only access to the Event"
            }
        );
    }
}