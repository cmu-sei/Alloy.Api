// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Alloy.Api.Data.Models;
using Alloy.Api.ViewModels;

namespace Alloy.Api.Infrastructure.Mappings
{
    public class UserProfile : AutoMapper.Profile
    {
        public UserProfile()
        {
            CreateMap<UserEntity, User>();
            CreateMap<User, UserEntity>();
        }
    }
}
