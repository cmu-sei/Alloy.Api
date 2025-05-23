// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.


using System;

namespace Alloy.Api.Infrastructure.Extensions
{
    public static class NotificationExtensions
    {
        public static string ViewBroadcastGroup(Guid id)
        {
            return "View_" + id.ToString();
        }

        public static string TeamBroadcastGroup(Guid id)
        {
            return "Team_" + id.ToString();
        }

        public static string UserBroadcastGroup(Guid id)
        {
            return "User_" + id.ToString();
        }

    }
}
