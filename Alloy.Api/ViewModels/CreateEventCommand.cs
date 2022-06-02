// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Alloy.Api.ViewModels
{
    public class CreateEventCommand
    {
        /// <summary>
        /// The ID of the EventTemplate to use to create the Event
        /// </summary>
        [JsonIgnore]
        public Guid EventTemplateId { get; set; }

        /// <summary>
        /// Id of the User that will be the owner of this Event
        /// </summary>
        public Guid? UserId { get; set; }

        /// <summary>
        /// Name of the User that will be the owner of this Event
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// List of Ids of additional Users to add to this Event
        /// </summary>
        public List<Guid> AdditionalUserIds { get; set; } = new List<Guid>();
    }
}