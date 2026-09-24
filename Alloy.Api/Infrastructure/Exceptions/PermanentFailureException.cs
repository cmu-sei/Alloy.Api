// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;

namespace Alloy.Api.Infrastructure.Exceptions
{
    /// <summary>
    /// Thrown when an operation cannot succeed no matter how many times it is retried - typically
    /// because an EventTemplate points at something that no longer exists or is misconfigured.
    /// <see cref="Extensions.ApiFailureExtensions"/> classifies this as
    /// <see cref="FailureKind.Permanent"/>, so the caller stops retrying and records the message.
    /// </summary>
    public class PermanentFailureException : Exception, IApiException
    {
        public PermanentFailureException(string message)
            : base(message)
        {
        }

        public PermanentFailureException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public HttpStatusCode GetStatusCode()
        {
            return HttpStatusCode.Conflict;
        }
    }
}
