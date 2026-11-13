// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Alloy.Api.Infrastructure.Options
{
    public class ResourceOptions
    {
        public int MaxEventsForBasicUser { get; set; }

        /// <summary>
        /// How long an ended or expired Event stays recoverable. During it the Event's Player view is
        /// set inactive and nothing is destroyed; after it, the Terraform workspace is destroyed and the
        /// view and scenario are removed. 0 or less tears down immediately.
        /// </summary>
        public int EndGracePeriodMinutes { get; set; }
    }
}
