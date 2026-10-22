// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Alloy.Api.Infrastructure.Exceptions;
using Alloy.Api.Infrastructure.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Alloy.Api.ViewModels;
using System;
using System.Net;

namespace Alloy.Api.Infrastructure.Filters
{
    public class JsonExceptionFilter : IExceptionFilter
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<JsonExceptionFilter> _logger;

        public JsonExceptionFilter(IWebHostEnvironment env, ILogger<JsonExceptionFilter> logger)
        {
            _env = env;
            _logger = logger;
        }

        public void OnException(ExceptionContext context)
        {
            var exception = context.Exception;

            // A Player, Caster or Steamfitter call that no service wrapped. Named here once so that
            // every call site gets a 502 naming the service rather than a 500.
            if (ApiFailureExtensions.ServiceName(exception) is { } service)
            {
                _logger.LogWarning(exception, "Unwrapped {Service} failure.", service);
                exception = exception.ToUserFacing(service, "complete the request");
            }

            var error = new ProblemDetails();
            error.Status = GetStatusCodeFromException(exception);

            if(error.Status == (int)HttpStatusCode.InternalServerError)
            {
                // Handling the exception here keeps it from the framework's own log.
                _logger.LogError(exception, "Unhandled exception.");

                if (_env.IsDevelopment())
                {
                    error.Title = exception.Message;
                    error.Detail = exception.StackTrace;
                }
                else
                {
                    error.Title = "A server error occurred.";
                    error.Detail = exception.Message;
                }
            }
            else
            {
                error.Title = exception.Message;
            }

            context.Result = new JsonResult(error)
            {
                StatusCode = error.Status
            };
        }

        /// <summary>
        /// map all custom exceptions to proper http status code
        /// </summary>
        /// <returns></returns>
        private static int GetStatusCodeFromException(Exception exception)
        {
            HttpStatusCode statusCode = HttpStatusCode.InternalServerError;

            if (exception is IApiException)
            {
                statusCode = (exception as IApiException).GetStatusCode();
            }

            return (int)statusCode;
        }
    }
}

