// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace Alloy.Api.Services
{
    /// <summary>
    /// Reads the zip archives an import accepts alongside plain JSON: a flat set of named entries, as
    /// earlier exports wrapped their JSON file.
    /// </summary>
    public interface IArchiveService
    {
        Task<Dictionary<string, byte[]>> ExtractZip(Stream archiveStream, CancellationToken ct);
    }

    public class ArchiveService : IArchiveService
    {
        /// <summary>
        /// Upper bound on the bytes read from an upload or extracted from an archive, in total, so a crafted
        /// file cannot exhaust memory.
        /// </summary>
        public const long MaxBytes = 10 * 1024 * 1024;

        /// <summary>Whether <paramref name="content"/> starts with a zip file's local file header signature.</summary>
        public static bool IsZip(ReadOnlySpan<byte> content) =>
            content.Length >= 4 && content[0] == 'P' && content[1] == 'K' && content[2] == 3 && content[3] == 4;

        public async Task<Dictionary<string, byte[]>> ExtractZip(Stream archiveStream, CancellationToken ct)
        {
            var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            long total = 0;

            using var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);

            foreach (var entry in zip.Entries)
            {
                // Directory entries have an empty Name
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                await using var entryStream = entry.Open();
                var bytes = await ReadBounded(entryStream, MaxBytes - total, ct);
                total += bytes.Length;
                entries[entry.FullName] = bytes;
            }

            return entries;
        }

        /// <summary>Reads <paramref name="source"/> to its end, failing once more than <paramref name="limit"/> bytes arrive.</summary>
        public static async Task<byte[]> ReadBounded(Stream source, long limit, CancellationToken ct)
        {
            using var target = new MemoryStream();
            var buffer = new byte[81920];
            int read;

            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                if (target.Length + read > limit)
                    throw new InvalidDataException($"The file is larger than {MaxBytes / (1024 * 1024)} MB.");

                target.Write(buffer, 0, read);
            }

            return target.ToArray();
        }
    }
}
