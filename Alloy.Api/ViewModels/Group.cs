// Copyright 2024 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;

namespace Alloy.Api.ViewModels
{
    public class Group : IAuthorizationType
    {
        /// <summary>
        /// ID of the group.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Name of the group.
        /// </summary>
        public string Name { get; set; }
    }
}