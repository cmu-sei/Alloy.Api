// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;

namespace Alloy.Api.Infrastructure.Exceptions
{
    /// <summary>
    /// Thrown when a request cannot be accepted because of something in the request itself -
    /// for example an EventTemplate whose Player View has no default team. Distinct from
    /// <see cref="PermanentFailureException"/>, which is classified by
    /// <see cref="Extensions.ApiFailureExtensions"/> for the background launch worker and
    /// should not be overloaded with request validation.
    /// </summary>
    public class ValidationException : Exception, IApiException
    {
        public ValidationException(string message)
            : base(message)
        {
        }

        public ValidationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public HttpStatusCode GetStatusCode()
        {
            return HttpStatusCode.BadRequest;
        }
    }
}
