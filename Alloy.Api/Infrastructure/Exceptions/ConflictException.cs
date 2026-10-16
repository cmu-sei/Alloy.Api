// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;

namespace Alloy.Api.Infrastructure.Exceptions
{
    /// <summary>
    /// Thrown when a request cannot be carried out in the current state of the thing it acts on -
    /// for example launching a second Event from a template that already has an active one, or
    /// redeploying an Event that is not active.
    /// </summary>
    public class ConflictException : Exception, IApiException
    {
        public ConflictException(string message)
            : base(message)
        {
        }

        public HttpStatusCode GetStatusCode()
        {
            return HttpStatusCode.Conflict;
        }
    }
}
