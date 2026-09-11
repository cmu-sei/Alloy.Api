// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Alloy.Api.Infrastructure
{
    /// <summary>
    /// How a failed call to an external Crucible API should be treated by the caller.
    /// </summary>
    public enum FailureKind
    {
        /// <summary>The call succeeded.</summary>
        None = 0,

        /// <summary>
        /// The call can be retried within the caller's retry budget.
        /// </summary>
        Transient,

        /// <summary>
        /// The operation should not be retried automatically. Launch failures enter cleanup;
        /// teardown uses its own retry policy.
        /// </summary>
        Permanent,

        /// <summary>Launch polling yielded to a user/expiration end request; not a failure.</summary>
        EndRequested
    }

    /// <summary>
    /// The outcome of a call to an external Crucible API (Player, Caster, Steamfitter).
    /// </summary>
    public class ApiCallResult
    {
        protected ApiCallResult(FailureKind kind, string summary, string detail)
        {
            Kind = kind;
            Summary = summary;
            Detail = detail;
        }

        public FailureKind Kind { get; }

        /// <summary>
        /// A short description of the failure that is safe to show to any user. Never contains
        /// Terraform output, response bodies, host names or variable values.
        /// </summary>
        public string Summary { get; }

        /// <summary>
        /// The full diagnostic text - Terraform plan/apply output, an API response body. Only ever
        /// shown to callers holding the system-wide ManageEvents permission.
        /// </summary>
        public string Detail { get; }

        public bool IsSuccess => Kind == FailureKind.None;
        public bool IsTransient => Kind == FailureKind.Transient;
        public bool IsPermanent => Kind == FailureKind.Permanent;
        public bool IsEndRequested => Kind == FailureKind.EndRequested;

        public static ApiCallResult Ok() => new(FailureKind.None, null, null);
        public static ApiCallResult EndRequested() => new(FailureKind.EndRequested, null, null);

        public static ApiCallResult Transient(string summary, string detail = null) =>
            new(FailureKind.Transient, summary, detail);

        public static ApiCallResult Permanent(string summary, string detail = null) =>
            new(FailureKind.Permanent, summary, detail);
    }

    /// <summary>
    /// An <see cref="ApiCallResult"/> that carries a value on success.
    /// </summary>
    public class ApiCallResult<T> : ApiCallResult
    {
        private ApiCallResult(FailureKind kind, T value, string summary, string detail)
            : base(kind, summary, detail)
        {
            Value = value;
        }

        /// <summary>
        /// The value produced by the call. Only meaningful when <see cref="ApiCallResult.IsSuccess"/>.
        /// </summary>
        public T Value { get; }

        public static ApiCallResult<T> Ok(T value) => new(FailureKind.None, value, null, null);

        public static new ApiCallResult<T> Transient(string summary, string detail = null) =>
            new(FailureKind.Transient, default, summary, detail);

        public static new ApiCallResult<T> Permanent(string summary, string detail = null) =>
            new(FailureKind.Permanent, default, summary, detail);
    }
}
