// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Alloy.Api.Data.Models
{
    public static class EventEntityExtensions
    {
        /// <summary>
        /// Clears previous errors and the launch-failure markers, so a later normal end
        /// is not reported as a failed launch.
        /// </summary>
        public static void ClearFailureState(this EventEntity eventEntity)
        {
            eventEntity.ErrorMessage = null;
            eventEntity.ErrorDetail = null;
            eventEntity.LastLaunchStatus = default;
            eventEntity.LastLaunchInternalStatus = default;
        }
    }
}
