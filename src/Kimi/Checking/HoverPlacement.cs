// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Documentation;

namespace Kimi.Checking;

/// <summary>An immutable, partial logical-to-physical map. It performs no file access.</summary>
internal sealed record HoverPlacement(string? Root, KeyValuePair<string, string?>[] Files)
{
    internal static readonly HoverPlacement Unsupported = new(null, []);

    internal string? Map(DocumentationLinkTarget target)
    {
        var low = 0;
        var high = this.Files.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var order = string.CompareOrdinal(target.LogicalPath, this.Files[middle].Key);
            if (order == 0)
            {
                return FileUri(this.Files[middle].Value);
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
            return relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative) ? null : FileUri(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    internal static string? FileUri(string? path)
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
