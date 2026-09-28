// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Security.Cryptography;

namespace Kimi.Compiler;

/// <summary>Shared artifact identity and path validation for emission and native builds.</summary>
internal static class ArtifactFiles
{
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
        if (string.IsNullOrWhiteSpace(path) || path.AsSpan().IndexOfAny("\0\r\n\"") >= 0 || path.StartsWith('-') ||
            (path.StartsWith('/') && !Path.IsPathFullyQualified(path)))
        {
            throw new InvalidDataException("Paths must be nonempty file/directory names, without embedded linker options.");
        }
    }
}
