// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Alloy.Api.Data.Models
{
    public static class EventEntityExtensions
    {
        /// <summary>
        /// Forgets everything recorded about a previous failure. Called from exactly three places -
        /// a successful launch, a successful redeploy, and the start of a redeploy - so that an old
        /// error cannot outlive the problem it described.
        /// <para>
        /// LastLaunchStatus/LastLaunchInternalStatus are cleared too, because
        /// AlloyBackgroundService reads them to tell a failed launch's teardown apart from a normal
        /// end. Leaving them set would make the next normal end report itself as Failed.
        /// </para>
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
