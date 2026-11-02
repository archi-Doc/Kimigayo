// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Documentation;

namespace Kimi.Checking;

/// <summary>An immutable, partial logical-to-physical map. It performs no file access when it maps.</summary>
internal sealed record HoverPlacement(string? Root, KeyValuePair<string, string?>[] Files)
{
    internal static readonly HoverPlacement Unsupported = new(null, []);

    // Each mapped file's identity, taken where the placement is built, so mapping a link reads no path.
    private readonly SourceIdentity?[] documents = Array.ConvertAll(
        Files, static x => x.Value is { } path && SourceIdentity.TryFromPath(path, out var document) ? document : (SourceIdentity?)null);

    /// <summary>Gets the URIs of a host without a client: each document's own (<see cref="SourceIdentity.ToUri"/>).</summary>
    internal static Func<SourceIdentity, string> Canonical { get; } = static document => document.ToUri();

    /// <summary>Maps a logical target to the URI of its physical file, which the host spells (<c>LspSession.UriOf</c>; DECISIONS
    /// R5-M180).</summary>
    /// <param name="target">The target.</param>
    /// <param name="uriOf">The host's URI of a document.</param>
    /// <returns>The URI, or null without a placement.</returns>
    internal string? Map(DocumentationLinkTarget target, Func<SourceIdentity, string> uriOf)
    {
        var low = 0;
        var high = this.Files.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) >> 1);
            var order = string.CompareOrdinal(target.LogicalPath, this.Files[middle].Key);
            if (order == 0)
            {
                return this.documents[middle] is { } document ? uriOf(document) : null;
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

        if (this.Root is not { } root || !SourceIdentity.TryFromPath(Path.Join(root, target.LogicalPath), out var placed))
        {
            return null;
        }

        try
        {
            var relative = Path.GetRelativePath(root, placed.Value);
            var outside = relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative);
            return outside ? null : uriOf(placed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
