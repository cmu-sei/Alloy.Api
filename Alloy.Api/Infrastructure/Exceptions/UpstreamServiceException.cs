// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;

namespace Alloy.Api.Infrastructure.Exceptions
{
    /// <summary>
    /// Thrown when Player, Caster or Steamfitter failed a call Alloy made while serving a request.
    /// The message names the service and what went wrong in terms the user can act on; the
    /// original exception is kept as the inner exception for the log.
    /// </summary>
    /// <remarks>
    /// Created by <see cref="Extensions.UpstreamErrorExtensions"/>. The background launch worker
    /// classifies its failures with <see cref="Extensions.ApiFailureExtensions"/> instead.
    /// </remarks>
    public class UpstreamServiceException : Exception, IApiException
    {
        private readonly HttpStatusCode _statusCode;

        public UpstreamServiceException(string message, Exception innerException)
            : this(message, innerException, HttpStatusCode.BadGateway)
        {
        }

        public UpstreamServiceException(string message, Exception innerException, HttpStatusCode statusCode)
            : base(message, innerException)
        {
            _statusCode = statusCode;
        }

        public HttpStatusCode GetStatusCode()
        {
            return _statusCode;
        }
    }
}
