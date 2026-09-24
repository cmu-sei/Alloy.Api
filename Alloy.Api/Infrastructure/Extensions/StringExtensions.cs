// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Text.RegularExpressions;

namespace Alloy.Api.Infrastructure.Extensions
{
    public static class StringExtensions
    {
        private const string TruncationPrefix = "...(truncated)" + "\n";
        private const string TruncationSuffix = "\n" + "...(truncated)";

        /// <summary>
        /// Terminal control sequences, as emitted by Terraform when Caster captures its coloured
        /// output: CSI sequences (colours, cursor moves, erase), OSC sequences (window titles), and
        /// bare carriage returns used to redraw a progress line in place.
        /// </summary>
        private static readonly Regex AnsiRegex = new(
            @"\x1B\[[0-9;?]*[ -/]*[@-~]" +      // CSI ... final byte
            @"|\x1B\][^\x07\x1B]*(?:\x07|\x1B\\)" + // OSC ... BEL or ST
            @"|\x1B[@-Z\\-_]" +                  // two-character escapes
            @"|\r(?!\n)",                        // bare CR (keep CRLF intact)
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Converts a string to CamelCase, assuming it is already in TitleCase
        /// </summary>
        public static string TitleCaseToCamelCase(this string str)
        {
            var camelCaseStr = str;

            if (!string.IsNullOrEmpty(str) && str.Length > 1)
            {
                camelCaseStr = Char.ToLowerInvariant(camelCaseStr[0]) + camelCaseStr.Substring(1);
            }

            return camelCaseStr;
        }

        /// <summary>
        /// Removes terminal control sequences so the text is readable in a browser or a log.
        /// </summary>
        public static string StripAnsi(this string str)
        {
            return string.IsNullOrEmpty(str) ? str : AnsiRegex.Replace(str, string.Empty);
        }

        /// <summary>
        /// Keeps at most <paramref name="maxLength"/> characters from the start of the string.
        /// </summary>
        public static string Truncate(this string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str) || str.Length <= maxLength)
            {
                return str;
            }

            // A cap smaller than the marker itself cannot carry the marker without breaking the
            // very limit the caller asked for, so in that degenerate case the raw text is simply
            // cut to length instead.
            if (maxLength < TruncationSuffix.Length)
            {
                return str.Substring(0, Math.Max(0, maxLength));
            }

            var keep = maxLength - TruncationSuffix.Length;
            return str.Substring(0, keep) + TruncationSuffix;
        }

        /// <summary>
        /// Keeps at most <paramref name="maxLength"/> characters from the *end* of the string.
        /// Terraform prints the actual error last, so the tail is the part worth keeping.
        /// </summary>
        public static string TruncateTail(this string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str) || str.Length <= maxLength)
            {
                return str;
            }

            // As above, a cap too small for the marker gets a plain cut of the text rather than a
            // marker that would exceed the limit; here the tail is kept, since that is the part
            // this method exists to preserve.
            if (maxLength < TruncationPrefix.Length)
            {
                var tail = Math.Max(0, maxLength);
                return str.Substring(str.Length - tail);
            }

            var keep = maxLength - TruncationPrefix.Length;
            return TruncationPrefix + str.Substring(str.Length - keep);
        }
    }
}
