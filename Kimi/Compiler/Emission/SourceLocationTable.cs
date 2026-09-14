// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Text;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

/// <summary>Displayed logical source locations for runtime diagnostics (SPEC 22.5.1, 22.5.4), cached per operation site.</summary>
internal sealed class SourceLocationTable
{
    private readonly Dictionary<(SourceDocument Source, int Offset), string> texts = new();
    private readonly StringBuilder buffer = new();
    private string? directory;

    /// <summary>Gets <c>path:line:column</c> with non-ASCII/control characters escaped as <c>\u{HEX}</c> and backslash as <c>\\</c>.</summary>
    /// <param name="node">The operation's source node.</param>
    /// <param name="projectDirectory">The directory that makes the displayed path independent of the checkout.</param>
    /// <param name="location">The displayed location.</param>
    /// <returns>Whether the node has source provenance.</returns>
    internal bool TryGet(Koto node, string projectDirectory, out string location)
    {
        if (!string.Equals(this.directory, projectDirectory, StringComparison.Ordinal))
        {
            this.texts.Clear();
            this.directory = projectDirectory;
        }

        if (node.CodeContext.SourceDocument is not { } source || (uint)node.Span.Start > (uint)source.SourceText.Length)
        {
            location = string.Empty;
            return false;
        }

        var key = (source, node.Span.Start);
        if (this.texts.TryGetValue(key, out location!))
        {
            return true;
        }

        var text = this.buffer;
        text.Clear();
        Span<char> hex = stackalloc char[8];
        var logicalPath = Path.IsPathFullyQualified(source.Path) && projectDirectory.Length > 0 ?
            Path.GetRelativePath(Path.GetFullPath(projectDirectory), source.Path) : source.Path;
        foreach (var rune in logicalPath.EnumerateRunes())
        {
            if (rune.Value == '\\')
            {
                text.Append("\\\\");
            }
            else if (rune.Value < 32 || rune.Value >= 127)
            {
                rune.Value.TryFormat(hex, out var count, "X", CultureInfo.InvariantCulture);
                text.Append("\\u{").Append(hex[..count]).Append('}');
            }
            else
            {
                text.Append((char)rune.Value);
            }
        }

        var position = source.GetPosition(node.Span.Start);
        text.Append(':').Append(position.Line + 1).Append(':').Append(position.Character + 1);
        location = text.ToString();
        this.texts.Add(key, location);
        return true;
    }
}
