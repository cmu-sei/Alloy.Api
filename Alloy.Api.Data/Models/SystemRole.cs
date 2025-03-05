// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Alloy.Api.Data.Models;

public class SystemRoleEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public bool AllPermissions { get; set; }
    public bool Immutable { get; set; }

    public List<SystemPermission> Permissions { get; set; }
}

public static class SystemRoleEntityDefaults
{
    public static Guid AdministratorRoleId = new("f35e8fff-f996-4cba-b303-3ba515ad8d2f");
    public static Guid ContentDeveloperRoleId = new("d80b73c3-95d7-4468-8650-c62bbd082507");
    public static Guid ObserverRoleId = new("1da3027e-725d-4753-9455-a836ed9bdb1e");
}

public class SystemRoleEntityConfiguration : IEntityTypeConfiguration<SystemRoleEntity>
{
    public void Configure(EntityTypeBuilder<SystemRoleEntity> builder)
    {
        builder.HasIndex(x => x.Name).IsUnique();

        builder.HasData(
            new SystemRoleEntity
            {
                Id = SystemRoleEntityDefaults.AdministratorRoleId,
                Name = "Administrator",
                AllPermissions = true,
                Immutable = true,
                Permissions = [],
                Description = "Can perform all actions"
            },
            new SystemRoleEntity
            {
                Id = SystemRoleEntityDefaults.ContentDeveloperRoleId,
                Name = "Content Developer",
                AllPermissions = false,
                Immutable = false,
                Permissions = [
                    SystemPermission.CreateEventTemplates,
                    SystemPermission.CreateEvents,
                    SystemPermission.ExecuteEvents
                ],
                Description = "Can create and manage their own Event Templates and Events."
            },
            new SystemRoleEntity
            {
                Id = SystemRoleEntityDefaults.ObserverRoleId,
                Name = "Observer",
                AllPermissions = false,
                Immutable = false,
                Permissions = Enum.GetValues<SystemPermission>()
                    .Where(x => x.ToString().StartsWith("View"))
                    .ToList(),
                Description = "Can View all Event Templates and Events, but cannot make any changes."
            }
        );
    }
}