// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Alloy.Api.Data.Models;
using System.Collections.Generic;

namespace Alloy.Api.Infrastructure.Options
{
    public class SeedDataOptions
    {
        public List<SystemRoleEntity> Roles { get; set; }
        public List<UserEntity> Users { get; set; }
        public List<GroupEntity> Groups { get; set; }
    }
}
