// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;

namespace Alloy.Api.Infrastructure.Options
{
    public class ClientOptions
    {
        public int BackgroundTimerIntervalSeconds { get; set; }
        public int BackgroundTimerHealthSeconds { get; set; }
        public int CasterCheckIntervalSeconds { get; set; }
        public int CasterPlanningMaxWaitMinutes { get; set; }
        public int CasterDeployMaxWaitMinutes { get; set; }
        public int CasterDestroyMaxWaitMinutes { get; set; }
        public int CasterDestroyRetryDelayMinutes { get; set; }
        public int ApiClientRetryIntervalSeconds { get; set; }
        public int ApiClientLaunchFailureMaxRetries { get; set; }
        public int ApiClientEndFailureMaxRetries { get; set; }
        public ApiUrlSettings urls { get; set; }

        /// <summary>
        /// Player users, such as the account Caster's Terraform provider signs in as, added to every View
        /// Alloy clones so they can manage it without a system-wide Player permission.
        /// </summary>
        public List<Guid> ClonedViewServiceUserIds { get; set; } = [];

        /// <summary>
        /// The Player team role whose team <see cref="ClonedViewServiceUserIds"/> join in each clone. Unset
        /// means the View's admin team, the first team whose role grants all permissions.
        /// </summary>
        public Guid? ClonedViewServiceTeamRoleId { get; set; }
    }

    public class ApiUrlSettings
    {
        public string playerApi { get; set; }
        public string casterApi { get; set; }
        public string steamfitterApi { get; set; }
    }
}
