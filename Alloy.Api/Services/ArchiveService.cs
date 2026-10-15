// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Alloy.Api.ViewModels;

namespace Alloy.Api.Services
{
    /// <summary>
    /// Writes and reads the zip and tar.gz archives used for export and import, in the same
    /// shape as Player's ArchiveService: a flat set of named entries.
    /// </summary>
    public interface IArchiveService
    {
        Task<ArchiveResult> ArchiveData(string archiveName, ArchiveType type, Dictionary<string, string> data, CancellationToken ct);
        Task<Dictionary<string, byte[]>> ExtractArchive(Stream archiveStream, string archiveFileName, CancellationToken ct);
    }

    public class ArchiveService : IArchiveService
    {
        /// <summary>
        /// Upper bound on the size of a single extracted entry, so a crafted archive cannot exhaust memory.
        /// </summary>
        public const long MaxEntryBytes = 10 * 1024 * 1024;

        public async Task<ArchiveResult> ArchiveData(string archiveName, ArchiveType type, Dictionary<string, string> data, CancellationToken ct)
        {
            var stream = new MemoryStream();

            switch (type)
            {
                case ArchiveType.zip:
                    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                    {
                        foreach (var (name, content) in data)
                        {
                            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                            await using var entryStream = entry.Open();
                            var bytes = Encoding.UTF8.GetBytes(content);
                            await entryStream.WriteAsync(bytes, ct);
                        }
                    }
                    break;
                case ArchiveType.tgz:
                    await using (var gzip = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true))
                    await using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
                    {
                        foreach (var (name, content) in data)
                        {
                            var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
                            {
                                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content))
                            };
                            await tar.WriteEntryAsync(entry, ct);
                        }
                    }
                    break;
                default:
                    throw new ArgumentException($"Unsupported archive type {type}");
            }

            stream.Position = 0;

            return new ArchiveResult
            {
                Data = stream,
                Name = $"{archiveName}.{type.GetExtension()}",
                Type = type.GetContentType()
            };
        }

        public async Task<Dictionary<string, byte[]>> ExtractArchive(Stream archiveStream, string archiveFileName, CancellationToken ct)
        {
            var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            switch (ArchiveTypeHelpers.GetType(archiveFileName))
            {
                case ArchiveType.zip:
                    using (var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true))
                    {
                        foreach (var entry in zip.Entries)
                        {
                            // Directory entries have an empty Name
                            if (string.IsNullOrEmpty(entry.Name))
                                continue;

                            await using var entryStream = entry.Open();
                            entries[entry.FullName] = await ReadBounded(entryStream, ct);
                        }
                    }
                    break;
                case ArchiveType.tgz:
                    await using (var gzip = new GZipStream(archiveStream, CompressionMode.Decompress, leaveOpen: true))
                    await using (var tar = new TarReader(gzip, leaveOpen: true))
                    {
                        while (await tar.GetNextEntryAsync(copyData: false, ct) is TarEntry entry)
                        {
                            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream == null)
                                continue;

                            entries[entry.Name] = await ReadBounded(entry.DataStream, ct);
                        }
                    }
                    break;
            }

            return entries;
        }

        private static async Task<byte[]> ReadBounded(Stream source, CancellationToken ct)
        {
            using var target = new MemoryStream();
            var buffer = new byte[81920];
            int read;

            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                if (target.Length + read > MaxEntryBytes)
                    throw new InvalidDataException($"An archive entry is larger than {MaxEntryBytes / (1024 * 1024)} MB.");

                target.Write(buffer, 0, read);
            }

            return target.ToArray();
        }
    }
}
