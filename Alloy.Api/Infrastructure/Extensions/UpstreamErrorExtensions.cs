// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.Infrastructure.Exceptions;
using Microsoft.Extensions.Logging;

namespace Alloy.Api.Infrastructure.Extensions
{
    /// <summary>
    /// Turns an exception from a Player, Caster or Steamfitter call made while serving a request into
    /// one the user can act on. Left alone, the generated clients' ApiException reaches
    /// JsonExceptionFilter as a 500 "A server error occurred." carrying nswag's
    /// "The HTTP status code of the response was not expected" text.
    /// </summary>
    /// <remarks>
    /// For requests only. The background launch worker decides whether to retry with
    /// <see cref="ApiFailureExtensions"/>, which is a different question.
    /// </remarks>
    public static class UpstreamErrorExtensions
    {
        /// <summary>
        /// Whether <paramref name="ex"/> is a failure of the call itself rather than a bug in Alloy.
        /// A cancellation counts only when the request was not cancelled, since that is how
        /// HttpClient reports a timeout.
        /// </summary>
        public static bool IsUpstreamFailure(this Exception ex, bool requestCancelled)
        {
            if (ex is OperationCanceledException)
                return !requestCancelled;

            return ApiFailureExtensions.IsConnectionFailure(ex) || ApiFailureExtensions.StatusCode(ex).HasValue;
        }

        /// <param name="ex">The exception that was caught.</param>
        /// <param name="service">"Player", "Caster" or "Steamfitter".</param>
        /// <param name="operation">
        /// What was being attempted, phrased to read after "Could not" - for example
        /// "load the Caster directories".
        /// </param>
        /// <remarks>
        /// Always a 502, whatever the service answered: a 404 from Caster passed on as Alloy's own
        /// would tell the client that the Alloy resource it asked for does not exist.
        /// </remarks>
        public static Exception ToUserFacing(this Exception ex, string service, string operation)
        {
            var statusCode = ApiFailureExtensions.StatusCode(ex);

            // nswag reports 0 when the request never produced a response at all.
            if (!statusCode.HasValue || statusCode == 0)
                return new UpstreamServiceException($"Could not {operation}. {service} could not be reached.", ex);

            var reason = statusCode.Value switch
            {
                // nswag throws on a success it cannot deserialize
                >= 200 and < 300 => $"{service} sent a response Alloy could not read.",
                (int)HttpStatusCode.Unauthorized => $"{service} did not accept your sign-in.",
                (int)HttpStatusCode.Forbidden => $"You do not have permission to do this in {service}.",
                (int)HttpStatusCode.NotFound => $"It was not found in {service}.",
                >= 500 => $"{service} reported an error (HTTP {statusCode}).",
                _ => $"{service} rejected the request (HTTP {statusCode})."
            };

            return new UpstreamServiceException($"Could not {operation}. {reason}", ex);
        }

        /// <summary>
        /// Runs <paramref name="call"/>, logging an upstream failure with its full detail and
        /// rethrowing it as <see cref="ToUserFacing"/> describes. Anything else propagates unchanged.
        /// </summary>
        public static async Task<T> CallUpstreamAsync<T>(this ILogger logger, string service, string operation,
            Func<Task<T>> call, CancellationToken ct)
        {
            try
            {
                return await call();
            }
            catch (Exception ex) when (ex.IsUpstreamFailure(ct.IsCancellationRequested))
            {
                logger.LogWarning(ex, "Could not {Operation} in {Service}.", operation, service);
                throw ex.ToUserFacing(service, operation);
            }
        }
    }
}
