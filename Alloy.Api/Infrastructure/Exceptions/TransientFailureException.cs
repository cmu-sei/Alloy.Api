// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;

namespace Alloy.Api.Infrastructure.Exceptions
{
    /// <summary>
    /// Thrown when an operation failed for a reason that may well succeed on a retry - a
    /// downstream API being briefly unavailable, a socket error, a timeout. The counterpart to
    /// <see cref="PermanentFailureException"/>, so that callers surfacing an
    /// <see cref="ApiCallResult"/> can map its <see cref="FailureKind"/> to an honest status code.
    /// </summary>
    public class TransientFailureException : Exception, IApiException
    {
        public TransientFailureException(string message)
            : base(message)
        {
        }

        public TransientFailureException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public HttpStatusCode GetStatusCode()
        {
            return HttpStatusCode.ServiceUnavailable;
        }
    }
}
