// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Checking;

/// <summary>Identifies one source input: a canonical file path, or a <c>compiler://</c> identifier of a built-in source.</summary>
/// <remarks>File paths compare with the platform comparer; <see cref="SourceIdentity"/> also names diagnostic locations.</remarks>
public readonly struct SourceIdentity : IEquatable<SourceIdentity>, IComparable<SourceIdentity>
{
    /// <summary>The scheme prefix of built-in sources.</summary>
    public const string BuiltInPrefix = "compiler://";

    private const string FilePrefix = "file:";

    /// <summary>Gets the path comparison of the current platform.</summary>
    public static StringComparison PathComparison { get; } = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Gets the path comparer of the current platform.</summary>
    public static StringComparer PathComparer { get; } = StringComparer.FromComparison(PathComparison);

    private SourceIdentity(string value)
    {
        this.Value = value;
    }

    /// <summary>Gets the canonical path or built-in identifier.</summary>
    public string Value { get; }

    /// <summary>Gets a value indicating whether this identity names a built-in source.</summary>
    public bool IsBuiltIn => this.Value.StartsWith(BuiltInPrefix, StringComparison.Ordinal);

    /// <summary>Gets a value indicating whether this identity is the default value.</summary>
    public bool IsEmpty => this.Value is null;

    /// <summary>Creates an identity from a file path or built-in identifier.</summary>
    /// <param name="path">The path; relative paths resolve against the current directory.</param>
    /// <returns>The canonical identity.</returns>
    public static SourceIdentity FromPath(string path)
        => new(path.StartsWith(BuiltInPrefix, StringComparison.Ordinal) ? path : Path.GetFullPath(path));

    /// <summary>Attempts to create an identity from a file path or built-in identifier: none for a path no file system names, such
    /// as one with NUL (an in-memory source), which no client can name either.</summary>
    /// <param name="path">The path; relative paths resolve against the current directory.</param>
    /// <param name="identity">The canonical identity.</param>
    /// <returns><see langword="true"/> when the path has an identity.</returns>
    public static bool TryFromPath(string path, out SourceIdentity identity)
    {
        try
        {
            identity = FromPath(path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            identity = default;
            return false;
        }
    }

    /// <summary>Attempts to convert a <c>file:</c> URI to an identity.</summary>
    /// <param name="uri">The client URI.</param>
    /// <param name="identity">The identity of the local path.</param>
    /// <returns><see langword="true"/> for a local <c>file:</c> URI.</returns>
    public static bool TryFromUri(string? uri, out SourceIdentity identity)
    {
        identity = default;
        if (uri is null || !uri.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsFile)
        {
            return false;
        }

        return TryFromPath(parsed.LocalPath, out identity);
    }

    /// <summary>Converts a file identity to its <c>file:</c> URI.</summary>
    /// <returns>The URI text.</returns>
    public string ToUri()
        => new Uri(this.Value).AbsoluteUri;

    /// <inheritdoc/>
    public bool Equals(SourceIdentity other)
        => PathComparer.Equals(this.Value, other.Value);

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is SourceIdentity other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => this.Value is null ? 0 : PathComparer.GetHashCode(this.Value);

    /// <inheritdoc/>
    public int CompareTo(SourceIdentity other)
        => PathComparer.Compare(this.Value, other.Value);

    /// <inheritdoc/>
    public override string ToString()
        => this.Value ?? string.Empty;

    public static bool operator ==(SourceIdentity left, SourceIdentity right)
        => left.Equals(right);

    public static bool operator !=(SourceIdentity left, SourceIdentity right)
        => !left.Equals(right);

    /// <summary>Gets where a fully qualified path's remainder under a base directory starts, or 0 when the base does not contain it:
    /// without allocation, the host's separators compare equal, letters as the host's paths do, and the prefix ends at a separator
    /// (<c>C:\p\App</c> holds no <c>C:\p\AppX</c>). No path makes it throw. The rule of a source's logical name
    /// (<c>Kotonoha.LogicalName</c>) and of a diagnostic's displayed path.</summary>
    /// <param name="path">The path.</param>
    /// <param name="directory">The base directory, normalized.</param>
    /// <returns>The remainder's start, or 0.</returns>
    internal static int Under(string path, string directory)
    {
        if (directory.Length == 0 || path.Length <= directory.Length || !Path.IsPathFullyQualified(path))
        {
            return 0;
        }

        for (var start = 0; ; start++)
        {
            var separator = directory.AsSpan(start).IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var end = separator < 0 ? directory.Length : start + separator;
            if (!directory.AsSpan(start, end - start).Equals(path.AsSpan(start, end - start), PathComparison))
            {
                return 0;
            }

            if (end == directory.Length)
            {
                if (IsSeparator(directory[^1]))
                {
                    return end; // A root base, such as C:\, ends in its separator.
                }

                return IsSeparator(path[end]) ? end + 1 : 0;
            }

            if (!IsSeparator(path[end]))
            {
                return 0;
            }

            start = end;
        }
    }

    private static bool IsSeparator(char value)
        => value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;
}
