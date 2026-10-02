// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Security.Cryptography;

namespace Kimi.Compiler;

/// <summary>Shared artifact identity and path validation for emission and native builds.</summary>
internal static class ArtifactFiles
{
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
                File.Move(source, destination, true);
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

    internal static void CheckPath(string path)
    {
        // A slash-prefixed linker option is also a fully qualified Unix path.
        var colon = path.IndexOf(':');
        var slashOption = path.StartsWith('/') && colon > 0 && path.AsSpan(1, colon - 1).IndexOfAny('/', '\\') < 0;
        if (string.IsNullOrWhiteSpace(path) || path.AsSpan().IndexOfAny("\0\r\n\"") >= 0 || path.StartsWith('-') ||
            slashOption || (path.StartsWith('/') && !Path.IsPathFullyQualified(path)))
        {
            throw new InvalidDataException("Paths must be nonempty file/directory names, without embedded linker options.");
        }
    }

    // ERROR_SHARING_VIOLATION (32) and ERROR_LOCK_VIOLATION (33); a missing source or directory is not transient.
    private static bool IsTransientReplaceFailure(Exception ex)
        => ex is UnauthorizedAccessException || (ex is IOException && (ex.HResult & 0xFFFF) is 32 or 33);
}
