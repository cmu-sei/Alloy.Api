// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.IO;
using System.Linq;

namespace Alloy.Api.ViewModels
{
    /// <summary>
    /// The archive formats an export can be written in. Matches Player and Caster.
    /// </summary>
    public enum ArchiveType
    {
        zip,
        tgz
    }

    public class ArchiveResult : IDisposable
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public Stream Data { get; set; }

        public void Dispose()
        {
            Data?.Close();
        }
    }

    public static class ArchiveTypeHelpers
    {
        public static string GetContentType(this ArchiveType archiveType) =>
            archiveType switch
            {
                ArchiveType.zip => "application/zip",
                ArchiveType.tgz => "application/gzip",
                _ => throw new ArgumentException($"Unsupported archive type {archiveType}")
            };

        public static string GetExtension(this ArchiveType archiveType) =>
            archiveType switch
            {
                ArchiveType.zip => "zip",
                ArchiveType.tgz => "tar.gz",
                _ => throw new ArgumentException($"Unsupported archive type {archiveType}")
            };

        public static string[] GetValidExtensions() => [".zip", ".tar.gz", ".tgz"];

        public static bool IsValidFileName(string fileName) =>
            !string.IsNullOrEmpty(fileName) &&
            GetValidExtensions().Any(x => fileName.EndsWith(x, StringComparison.OrdinalIgnoreCase));

        public static ArchiveType GetType(string fileName)
        {
            var name = fileName?.ToLowerInvariant() ?? string.Empty;

            if (name.EndsWith(".zip"))
                return ArchiveType.zip;

            if (name.EndsWith(".tar.gz") || name.EndsWith(".tgz"))
                return ArchiveType.tgz;

            throw new ArgumentException($"File extension must be one of {string.Join(", ", GetValidExtensions())}");
        }
    }
}
