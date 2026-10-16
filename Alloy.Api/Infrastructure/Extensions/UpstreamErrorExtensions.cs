// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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
        /// A TaskCanceledException counts only when the request was not cancelled, since that is how
        /// HttpClient reports a timeout.
        /// </summary>
        public static bool IsUpstreamFailure(this Exception ex, bool requestCancelled)
        {
            if (ex is TaskCanceledException)
                return !requestCancelled;

            return ex is HttpRequestException ||
                ex is SocketException ||
                ex is TimeoutException ||
                StatusCode(ex).HasValue;
        }

        /// <param name="ex">The exception that was caught.</param>
        /// <param name="service">"Player", "Caster" or "Steamfitter".</param>
        /// <param name="operation">
        /// What was being attempted, phrased to read after "Could not" - for example
        /// "load the Caster directories".
        /// </param>
        public static Exception ToUserFacing(this Exception ex, string service, string operation)
        {
            var statusCode = StatusCode(ex);

            // nswag reports 0 when the request never produced a response at all.
            if (!statusCode.HasValue || statusCode == 0)
                return new UpstreamServiceException($"Could not {operation}. {service} could not be reached.", ex);

            switch (statusCode.Value)
            {
                case (int)HttpStatusCode.Unauthorized:
                    return new UpstreamServiceException($"Could not {operation}. {service} did not accept your sign-in.", ex);
                case (int)HttpStatusCode.Forbidden:
                    return new UpstreamServiceException(
                        $"Could not {operation}. You do not have permission to do this in {service}.", ex, HttpStatusCode.Forbidden);
                case (int)HttpStatusCode.NotFound:
                    return new UpstreamServiceException(
                        $"Could not {operation}. It was not found in {service}.", ex, HttpStatusCode.NotFound);
                default:
                    return statusCode >= 500
                        ? new UpstreamServiceException($"Could not {operation}. {service} reported an error (HTTP {statusCode}).", ex)
                        : new UpstreamServiceException($"Could not {operation}. {service} rejected the request (HTTP {statusCode}).", ex);
            }
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

        // The three generated clients each declare their own ApiException with no shared base class.
        private static int? StatusCode(Exception ex) => ex switch
        {
            Caster.Api.Client.ApiException caster => caster.StatusCode,
            Player.Api.Client.ApiException player => player.StatusCode,
            Steamfitter.Api.Client.ApiException steamfitter => steamfitter.StatusCode,
            _ => null
        };
    }
}
