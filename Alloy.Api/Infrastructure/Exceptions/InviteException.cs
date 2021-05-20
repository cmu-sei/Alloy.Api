// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Text.RegularExpressions;

namespace Alloy.Api.Infrastructure.Exceptions
{
    public class InviteException : Exception, IApiException
    {
        Guid _eventId;
        public InviteException()
            : base("Invite Failed")
        {
        }

        public InviteException(string message)
            : base(message)
        {
        }
        public HttpStatusCode GetStatusCode()
        {
            return HttpStatusCode.Conflict;
        }
    }
}

