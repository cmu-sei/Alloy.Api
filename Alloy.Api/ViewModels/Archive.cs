// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Alloy.Api.ViewModels
{
    /// <summary>A file written by an export: its download name, content type and bytes.</summary>
    public record ExportFile(string Name, string ContentType, byte[] Data);
}
