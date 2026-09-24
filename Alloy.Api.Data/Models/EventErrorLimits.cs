// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Alloy.Api.Data.Models
{
    /// <summary>
    /// Caps on the size of the failure text stored against an Event.
    /// <para>
    /// Both columns are plain <c>text</c> in the database; these limits are enforced in code at the
    /// few places that write them. Terraform plan and apply output routinely runs to hundreds of KB,
    /// and <see cref="EventEntity.ErrorMessage"/> is broadcast over SignalR on every save, so an
    /// unbounded value is a real problem for both the database and every connected client.
    /// </para>
    /// </summary>
    public static class EventErrorLimits
    {
        /// <summary>
        /// Maximum length of the user-facing summary. Long enough for a sentence or two.
        /// </summary>
        public const int MaxSummaryLength = 512;

        /// <summary>
        /// Maximum length of the admin-only diagnostic detail. Terraform prints the actual error
        /// last, so the tail is what gets kept.
        /// </summary>
        public const int MaxDetailLength = 8 * 1024;
    }
}
