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
        /// The call may well succeed if it is tried again - a network blip, an expired token,
        /// a 5xx, or an operation that is simply not finished yet. The caller retries until it
        /// runs out of its retry budget.
        /// </summary>
        Transient,

        /// <summary>
        /// The call will never succeed no matter how many times it is tried - a template pointing
        /// at an object that has been deleted, invalid Terraform, a rejected run. The caller gives
        /// up immediately and records why.
        /// </summary>
        Permanent
    }

    /// <summary>
    /// The outcome of a call to an external Crucible API (Player, Caster, Steamfitter).
    /// <para>
    /// This is a class rather than a tuple or a record struct on purpose: a default-constructed
    /// struct would have <see cref="FailureKind.None"/> and so would read as success, which is
    /// exactly the mistake this type exists to prevent.
    /// </para>
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

        public static ApiCallResult Ok() => new(FailureKind.None, null, null);

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
