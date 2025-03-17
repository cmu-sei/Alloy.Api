// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Alloy.Api.Data.Models;

public class EventTemplateRoleEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    public bool AllPermissions { get; set; }

    public List<EventTemplatePermission> Permissions { get; set; }
}

public static class EventTemplateRoleEntityDefaults
{
    public static Guid EventTemplateCreatorRoleId = new("1a3f26cd-9d99-4b98-b914-12931e786198");
    public static Guid EventTemplateReadOnlyRoleId = new("39aa296e-05ba-4fb0-8d74-c92cf3354c6f");
    public static Guid EventTemplateMemberRoleId = new("f870d8ee-7332-4f7f-8ee0-63bd07cfd7e4");
}

public class EventTemplateRoleEntityConfiguration : IEntityTypeConfiguration<EventTemplateRoleEntity>
{
    public void Configure(EntityTypeBuilder<EventTemplateRoleEntity> builder)
    {
        builder.HasData(
            new EventTemplateRoleEntity
            {
                Id = EventTemplateRoleEntityDefaults.EventTemplateCreatorRoleId,
                Name = "Manager",
                AllPermissions = true,
                Permissions = [],
                Description = "Can perform all actions on the EventTemplate"
            },
            new EventTemplateRoleEntity
            {
                Id = EventTemplateRoleEntityDefaults.EventTemplateReadOnlyRoleId,
                Name = "Observer",
                AllPermissions = false,
                Permissions = [EventTemplatePermission.ViewEventTemplate],
                Description = "Has read only access to the EventTemplate"
            },
            new EventTemplateRoleEntity
            {
                Id = EventTemplateRoleEntityDefaults.EventTemplateMemberRoleId,
                Name = "Member",
                AllPermissions = false,
                Permissions = [
                    EventTemplatePermission.ViewEventTemplate,
                    EventTemplatePermission.EditEventTemplate
                ],
                Description = "Has read only access to the EventTemplate"
            }
        );
    }
}