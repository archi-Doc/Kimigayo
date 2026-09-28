// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Checking;

namespace Kimi.Lsp;

/// <summary>Reads disk inputs as observations (SPEC 23.3.4): absence, content or the failure kind and message.</summary>
internal static class DiskReader
{
    /// <summary>Merges matching open documents into a retained disk listing, preserving canonical disk spellings.</summary>
    /// <param name="key">The listing key.</param>
    /// <param name="disk">The disk names, possibly with an earlier overlay in <see cref="InputState.Names"/>.</param>
    /// <param name="openPaths">The open documents in this directory.</param>
    /// <returns>The unchanged disk state when possible, or a sorted union without duplicate file identities.</returns>
    public static InputState MergeListing(InputKey key, InputState disk, ReadOnlySpan<string> openPaths)
    {
        if (!disk.Established)
        {
            return disk;
        }

        var names = disk.DiskNames!;
        List<string>? added = null;
        foreach (var path in openPaths)
        {
            if (!path.AsSpan().EndsWith(key.Pattern.AsSpan(1), StringComparison.OrdinalIgnoreCase) ||
                Array.BinarySearch(names, path, StringComparer.Ordinal) >= 0 ||
                (OperatingSystem.IsWindows() && ContainsPath(names, path)) ||
                (added is not null && ContainsPath(CollectionsMarshal.AsSpan(added), path)))
            {
                continue;
            }

            (added ??= []).Add(path);
        }

        if (added is null)
        {
            return ReferenceEquals(disk.Names, names) ? disk : new() { Names = names, DiskNames = names, Stamp = disk.Stamp };
        }

        added.Sort(StringComparer.Ordinal);
        var merged = new string[names.Length + added.Count];
        int source = 0, destination = 0;
        foreach (var path in added)
        {
            while (source < names.Length && string.CompareOrdinal(names[source], path) < 0)
            {
                merged[destination++] = names[source++];
            }

            merged[destination++] = path;
        }

        names.AsSpan(source).CopyTo(merged.AsSpan(destination));
        return new() { Names = merged, DiskNames = names, Stamp = disk.Stamp };
    }

    /// <summary>Reads a file.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The state.</returns>
    public static InputState ReadFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var stamp = info.LastWriteTimeUtc.Ticks;
            var bytes = File.ReadAllBytes(path);
            return new() { Content = SourceContent.FromBytes(bytes), Stamp = stamp, Length = bytes.Length };
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
            // Unix reports ENOTDIR as DirectoryNotFoundException; an existing file is not an absent directory.
            if ((File.GetAttributes(directory) & FileAttributes.Directory) == 0)
            {
                return InputState.Unestablished("IOException: The listing path is not a directory.");
            }

            var stamp = Directory.GetLastWriteTimeUtc(directory).Ticks;
            var files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.Ordinal);
            return new() { DiskNames = files, Names = files, Stamp = stamp };
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new() { DiskNames = [], Names = [] };
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

    private static bool ContainsPath(ReadOnlySpan<string> names, string path)
    {
        foreach (var name in names)
        {
            if (SourceIdentity.PathComparer.Equals(name, path))
            {
                return true;
            }
        }

        return false;
    }
}
