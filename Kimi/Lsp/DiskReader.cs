// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Checking;

namespace Kimi.Lsp;

/// <summary>Reads disk inputs as observations (SPEC 23.3.4): absence, content or the failure kind and message.</summary>
internal static class DiskReader
{
    /// <summary>Reads a file.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The state.</returns>
    public static InputState ReadFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return new() { Absent = true };
            }

            var bytes = File.ReadAllBytes(path);
            return new() { Content = SourceContent.FromBytes(bytes), Stamp = info.LastWriteTimeUtc.Ticks, Length = bytes.Length };
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new() { Absent = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return InputState.Unestablished(ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>Reads a directory listing.</summary>
    /// <param name="directory">The directory.</param>
    /// <param name="pattern">The pattern.</param>
    /// <returns>The state, with the disk names in ordinal order.</returns>
    public static InputState ReadListing(string directory, string pattern)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return new() { DiskNames = [], Names = [] };
            }

            var stamp = Directory.GetLastWriteTimeUtc(directory).Ticks;
            var files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.Ordinal);
            return new() { DiskNames = files, Names = files, Stamp = stamp };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return InputState.Unestablished(ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>Reads the disk stamp of a file.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The timestamp ticks and length, or zero and -1 when the file is absent or unreadable.</returns>
    public static (long Stamp, long Length) Stat(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.LastWriteTimeUtc.Ticks, info.Length) : (0, -1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (0, -1);
        }
    }

    /// <summary>Reads the disk timestamp of a directory.</summary>
    /// <param name="directory">The directory.</param>
    /// <returns>The ticks, or 0 when absent or unreadable.</returns>
    public static long DirectoryStamp(string directory)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.GetLastWriteTimeUtc(directory).Ticks : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return 0;
        }
    }

    /// <summary>Reads whether a file begins with a UTF-8 BOM.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The flag; false when the file is absent or unreadable.</returns>
    public static bool HasBom(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);
            Span<byte> head = stackalloc byte[3];
            return stream.ReadAtLeast(head, 3, false) == 3 && head.SequenceEqual("﻿"u8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
