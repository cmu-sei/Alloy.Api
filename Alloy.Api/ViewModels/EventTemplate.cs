// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Alloy.Api.Data.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Alloy.Api.ViewModels
{
    public class EventTemplate : Base
    {
        public Guid Id { get; set; }
        public Guid? ViewId { get; set; }
        public Guid? DirectoryId { get; set; }
        public Guid? ScenarioTemplateId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int DurationHours { get; set; }
        public bool UseDynamicHost { get; set; }
        public bool IsPublished { get; set; }
    }
}
