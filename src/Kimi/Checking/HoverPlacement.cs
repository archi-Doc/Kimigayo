// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Documentation;

namespace Kimi.Checking;

/// <summary>An immutable, partial logical-to-physical map. It performs no file access.</summary>
internal sealed record HoverPlacement(string? Root, KeyValuePair<string, string?>[] Files)
{
    internal static readonly HoverPlacement Unsupported = new(null, []);

    /// <summary>Gets the lookup of a host without a client, which names no document.</summary>
    internal static Func<SourceIdentity, string?> Unnamed { get; } = static _ => null;

    /// <summary>Maps a logical target to the URI of its physical file: the URI a client opened the document with (from
    /// <paramref name="named"/>; DECISIONS R5-M180), else its file URI.</summary>
    /// <param name="target">The target.</param>
    /// <param name="named">The client's URI of a document it opened, or null.</param>
    /// <returns>The URI, or null without a placement.</returns>
    internal string? Map(DocumentationLinkTarget target, Func<SourceIdentity, string?> named)
    {
        var low = 0;
        var high = this.Files.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var order = string.CompareOrdinal(target.LogicalPath, this.Files[middle].Key);
            if (order == 0)
            {
                return Link(this.Files[middle].Value, named);
            }

            if (order < 0)
            {
                high = middle - 1;
            }
            else
            {
                low = middle + 1;
            }
        }

        if (this.Root is not { } root)
        {
            return null;
        }

        try
        {
            var path = Path.GetFullPath(target.LogicalPath, root);
            var relative = Path.GetRelativePath(root, path);
            var outside = relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative);
            return outside ? null : Link(path, named);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? Link(string? path, Func<SourceIdentity, string?> named)
        => path is not null && SourceIdentity.TryFromPath(path, out var document) && named(document) is { } uri
            ? uri : FileUri(path);

    private static string? FileUri(string? path)
    {
        if (path is null || !Path.IsPathFullyQualified(path))
        {
            return null;
        }

        // Uri otherwise treats a literal %20 in a file name as an existing escape. Protect path data before URI parsing.
        var normalized = OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;
        var escaped = normalized.Replace("%", "%25", StringComparison.Ordinal).Replace("#", "%23", StringComparison.Ordinal).Replace("?", "%3F", StringComparison.Ordinal);
        var prefix = normalized.StartsWith("//", StringComparison.Ordinal) ? "file:" : normalized.StartsWith('/') ? "file://" : "file:///";
        return Uri.TryCreate(prefix + escaped, UriKind.Absolute, out var uri) && uri.IsFile && SourceIdentity.PathComparer.Equals(uri.LocalPath, path)
            ? uri.AbsoluteUri : null;
    }
}
