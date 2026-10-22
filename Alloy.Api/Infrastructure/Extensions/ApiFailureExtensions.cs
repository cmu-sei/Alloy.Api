// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Alloy.Api.Infrastructure.Exceptions;

namespace Alloy.Api.Infrastructure.Extensions
{
    /// <summary>
    /// The single place where an exception from an external Crucible API is judged to be worth
    /// retrying or not. Everything that talks to Player, Caster or Steamfitter funnels its
    /// exceptions through here so that the decision is made once, consistently.
    /// </summary>
    public static class ApiFailureExtensions
    {
        /// <summary>
        /// Classifies an exception raised while calling an external API.
        /// </summary>
        /// <param name="ex">The exception that was caught.</param>
        /// <param name="operation">
        /// A short, user-safe description of what was being attempted, phrased to read after
        /// "Failed to" - for example "create the virtual environment".
        /// </param>
        public static ApiCallResult Classify(this Exception ex, string operation)
        {
            var (kind, summary, detail) = Judge(ex, operation);

            return kind == FailureKind.Permanent
                ? ApiCallResult.Permanent(summary, detail)
                : ApiCallResult.Transient(summary, detail);
        }

        /// <summary>
        /// Classifies an exception raised while calling an external API, for a call that would
        /// have produced a value.
        /// </summary>
        public static ApiCallResult<T> Classify<T>(this Exception ex, string operation)
        {
            var (kind, summary, detail) = Judge(ex, operation);

            return kind == FailureKind.Permanent
                ? ApiCallResult<T>.Permanent(summary, detail)
                : ApiCallResult<T>.Transient(summary, detail);
        }

        private static (FailureKind kind, string summary, string detail) Judge(Exception ex, string operation)
        {
            // A template pointing at something that has been deleted, or otherwise unusable. There
            // is nothing to wait for.
            if (ex is PermanentFailureException)
            {
                return (FailureKind.Permanent, $"Failed to {operation}. {ex.Message}", BuildDetail(ex, null));
            }

            // The connection never completed, or completed badly. Always worth another try.
            if (IsConnectionFailure(ex))
            {
                return (FailureKind.Transient,
                    $"Failed to {operation}. The service could not be reached; retrying.",
                    BuildDetail(ex, null));
            }

            var statusCode = StatusCode(ex);

            if (statusCode.HasValue)
            {
                return ClassifyStatusCode(statusCode.Value, operation, BuildDetail(ex, Response(ex)));
            }

            // Deliberately transient: treating an unrecognized exception as permanent is what makes
            // a single unexpected blip destroy someone's event. The retry ceiling still guarantees
            // that the event terminates.
            return (FailureKind.Transient,
                $"Failed to {operation}. An unexpected error occurred; retrying.",
                BuildDetail(ex, null));
        }

        /// <summary>
        /// Whether the call never got a usable response: the connection failed, broke or timed out.
        /// An OperationCanceledException counts, since that is how HttpClient reports a timeout; a
        /// caller that can also be cancelled has to rule that out itself.
        /// </summary>
        internal static bool IsConnectionFailure(Exception ex) =>
            ex is HttpRequestException ||
            ex is SocketException ||
            ex is IOException ||
            ex is OperationCanceledException || // includes TaskCanceledException
            ex is TimeoutException;

        // The three generated clients each declare their own ApiException with no shared base
        // class, so each one has to be named explicitly.

        /// <summary>The HTTP status of a generated client's ApiException, or null for any other exception.</summary>
        internal static int? StatusCode(Exception ex) => ex switch
        {
            Caster.Api.Client.ApiException caster => caster.StatusCode,
            Player.Api.Client.ApiException player => player.StatusCode,
            Steamfitter.Api.Client.ApiException steamfitter => steamfitter.StatusCode,
            _ => null
        };

        /// <summary>The service a generated client's ApiException came from, or null for any other exception.</summary>
        internal static string ServiceName(Exception ex) => ex switch
        {
            Caster.Api.Client.ApiException => "Caster",
            Player.Api.Client.ApiException => "Player",
            Steamfitter.Api.Client.ApiException => "Steamfitter",
            _ => null
        };

        private static string Response(Exception ex) => ex switch
        {
            Caster.Api.Client.ApiException caster => caster.Response,
            Player.Api.Client.ApiException player => player.Response,
            Steamfitter.Api.Client.ApiException steamfitter => steamfitter.Response,
            _ => null
        };

        private static (FailureKind kind, string summary, string detail) ClassifyStatusCode(
            int statusCode, string operation, string detail)
        {
            switch (statusCode)
            {
                // nswag reports 0 when the request never produced a response at all.
                case 0:
                // The resource-owner token has expired or has not been granted yet. The caller
                // discards its token and acquires a new one on the next pass.
                case (int)HttpStatusCode.Unauthorized:
                case (int)HttpStatusCode.Forbidden:
                    return (FailureKind.Transient,
                        $"Failed to {operation}. Authorization was refused; retrying.",
                        detail);

                case (int)HttpStatusCode.RequestTimeout:
                case 425: // Too Early - not in HttpStatusCode
                case (int)HttpStatusCode.TooManyRequests:
                    return (FailureKind.Transient,
                        $"Failed to {operation}. The service is busy; retrying.",
                        detail);

                // A template referring to a view, directory or scenario template that has been
                // deleted lands here, and no amount of retrying will bring it back.
                case (int)HttpStatusCode.BadRequest:
                case (int)HttpStatusCode.NotFound:
                case (int)HttpStatusCode.MethodNotAllowed:
                case (int)HttpStatusCode.Conflict:
                case (int)HttpStatusCode.Gone:
                case (int)HttpStatusCode.UnsupportedMediaType:
                case (int)HttpStatusCode.UnprocessableEntity:
                    return (FailureKind.Permanent,
                        $"Failed to {operation}. The request was rejected (HTTP {statusCode}). " +
                        "The event template may refer to something that no longer exists.",
                        detail);

                default:
                    if (statusCode >= 500)
                    {
                        return (FailureKind.Transient,
                            $"Failed to {operation}. The service reported an error (HTTP {statusCode}); retrying.",
                            detail);
                    }

                    return (FailureKind.Transient,
                        $"Failed to {operation}. Unexpected response (HTTP {statusCode}); retrying.",
                        detail);
            }
        }

        private static string BuildDetail(Exception ex, string response)
        {
            // ApiException truncates the response body inside its own Message, so the raw Response
            // is spelled out separately.
            var detail = ex.ToString();

            if (!string.IsNullOrWhiteSpace(response))
            {
                detail += $"{Environment.NewLine}{Environment.NewLine}Response:{Environment.NewLine}{response}";
            }

            return detail;
        }
    }
}
