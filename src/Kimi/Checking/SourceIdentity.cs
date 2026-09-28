// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Checking;

/// <summary>Identifies one source input: a canonical file path, or a <c>compiler://</c> identifier of a built-in source.</summary>
/// <remarks>File paths compare with the platform comparer; <see cref="SourceIdentity"/> also names diagnostic locations.</remarks>
public readonly struct SourceIdentity : IEquatable<SourceIdentity>, IComparable<SourceIdentity>
{
    /// <summary>The scheme prefix of built-in sources.</summary>
    public const string BuiltInPrefix = "compiler://";

    private const string FilePrefix = "file:";

    /// <summary>Gets the path comparer of the current platform.</summary>
    public static StringComparer PathComparer { get; } = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

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

        try
        {
            identity = FromPath(parsed.LocalPath);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
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
}
