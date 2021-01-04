// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Alloy.Api.Infrastructure.Options
{
    public class ClientOptions
    {
        public int BackgroundTimerIntervalSeconds { get; set; }
        public int CasterCheckIntervalSeconds { get; set; }
        public int CasterPlanningMaxWaitMinutes { get; set; }
        public int CasterDeployMaxWaitMinutes { get; set; }
        public int CasterDestroyMaxWaitMinutes { get; set; }
        public int CasterDestroyRetryDelayMinutes { get; set; }
        public int ApiClientRetryIntervalSeconds { get; set; }
        public int ApiClientLaunchFailureMaxRetries { get; set; }
        public int ApiClientEndFailureMaxRetries { get; set; }
        public ApiUrlSettings urls { get; set; }
    }

    public class ApiUrlSettings
    {
        public string playerApi { get; set; }
        public string casterApi { get; set; }
        public string steamfitterApi { get; set; }
    }
}
