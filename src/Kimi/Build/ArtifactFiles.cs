// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kimi.Build;

/// <summary>Shared artifact identity and path validation for emission and native builds: an artifact file's replacement, hash and path,
/// and whether a manifest's or record's JSON restates the strings its reader expects.</summary>
internal static class ArtifactFiles
{
    // The encoding of every text artifact and record: UTF-8 without a byte order mark.
    internal static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    // Total retry wait stays below one second; observed holds end within about 20 ms.
    private const int ReplaceDelayLimitMilliseconds = 512;

    // Atomically replaces destination with source. Windows briefly keeps a just-published file open (real-time
    // scanning, indexing), and replacing it during that hold fails with access denied or a sharing violation.
    // The hold is transient, so the rename is retried with a bounded backoff before the failure is reported.
    internal static void Replace(string source, string destination)
    {
        for (var delay = 1; ; delay *= 2)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when (delay <= ReplaceDelayLimitMilliseconds && IsTransientReplaceFailure(ex) && !Directory.Exists(destination))
            {
                Thread.Sleep(delay);
            }
        }
    }

    internal static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(stream, hash);
        return Convert.ToHexStringLower(hash);
    }

    internal static string ResolvePath(string value, string directory)
    {
        CheckPath(value);
        return Path.GetFullPath(value, directory);
    }

    /// <summary>Whether a JSON object states each named string with its value, read in the rows' order up to the first difference.</summary>
    /// <param name="element">The object.</param>
    /// <param name="rows">The names and their expected values.</param>
    /// <returns>Whether every row matches.</returns>
    internal static bool States(JsonElement element, params ReadOnlySpan<(string Name, string Value)> rows)
    {
        foreach (var (name, value) in rows)
        {
            if (element.GetProperty(name).GetString() != value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares a JSON string array with an expected ordered list without allocating.</summary>
    /// <param name="array">The JSON array.</param>
    /// <param name="expected">The expected strings.</param>
    /// <returns>Whether both contain the same strings in order.</returns>
    internal static bool SequenceEqual(JsonElement array, ReadOnlySpan<string> expected)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != expected.Length)
        {
            return false;
        }

        var i = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !item.ValueEquals(expected[i++]))
            {
                return false;
            }
        }

        return true;
    }

    // A failure of an output file or directory, which its writer reports as the output's (DECISIONS R5-M169).
    internal static bool IsOutputFailure(Exception ex) => ex is IOException or UnauthorizedAccessException;

    internal static void CheckPath(string path)
    {
        // An option: a leading '-', or a slash-prefixed linker option, which is also a fully qualified Unix path.
        var colon = path.IndexOf(':');
        var option = path.StartsWith('-') || (path.StartsWith('/') && colon > 0 && path.AsSpan(1, colon - 1).IndexOfAny('/', '\\') < 0);
        if (string.IsNullOrWhiteSpace(path) || path.AsSpan().IndexOfAny("\0\r\n\"") >= 0 || option ||
            (path.StartsWith('/') && !Path.IsPathFullyQualified(path)))
        {
            throw new InvalidDataException("Paths must be nonempty file/directory names, without embedded linker options.");
        }
    }

    // ERROR_SHARING_VIOLATION (32) and ERROR_LOCK_VIOLATION (33); a missing source or directory is not transient.
    private static bool IsTransientReplaceFailure(Exception ex)
        => ex is UnauthorizedAccessException || (ex is IOException && (ex.HResult & 0xFFFF) is 32 or 33);
}
