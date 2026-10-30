// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the runtime location text of a syntax site, the `path:line:column` an Abort reports (SPEC 22.5.1, IMPL 21.4.2).
// Input: a SourceRef of this compilation. Output: the logical name of its source (IMPL 20.1), escaped, with its line and column.
// Texts are memoized per (document, offset) of the current program: the memo is cleared when the source analysis is revoked
// (an edit, a parse or a reload; Compilation.InvalidateSourceAnalysis) and when the project directory changes.
// Must not key the memo by SourceRef (ids renumber on edit), decide which site an Abort reports (its generator's rule) or
// render the test manifest's location (TestCatalog, SPEC 20.6).

using System.Globalization;
using System.Text;

namespace Kimi.Compiler;

/// <summary>The runtime location texts of one compilation (SPEC 22.5.1), memoized per source position of its current program.</summary>
/// <param name="compilation">The compilation whose syntax the sites name.</param>
internal sealed class SourceLocationTable(Compilation compilation)
{
    private readonly Dictionary<(SourceDocument Source, int Offset), string> texts = new();
    private readonly StringBuilder buffer = new();
    private string? directory;

    /// <summary>Gets <c>path:line:column</c>: the logical name of the site's source, its project-relative path with <c>/</c>
    /// separators (IMPL 20.1; a project without a directory is the current directory's), with non-ASCII and control characters
    /// escaped as <c>\u{HEX}</c> and backslash as <c>\\</c> (IMPL 21.4.2), then the site's line and column.</summary>
    /// <param name="site">The site.</param>
    /// <returns>The text, or <see langword="null"/> when the site has no source document, a broken contract its reader reports.</returns>
    internal string? Text(SourceRef site)
    {
        var directory = compilation.Project.Directory;
        if (!string.Equals(this.directory, directory, StringComparison.Ordinal))
        {
            this.texts.Clear();
            this.directory = directory;
        }

        var node = compilation.SyntaxOf(site);
        if (node.CodeContext.SourceDocument is not { } source || (uint)node.Span.Start > (uint)source.SourceText.Length)
        {
            return null;
        }

        var key = (source, node.Span.Start);
        if (this.texts.TryGetValue(key, out var text))
        {
            return text;
        }

        var root = directory.Length > 0 ? directory : ".";
        var path = Path.IsPathFullyQualified(source.Path) ? Path.GetRelativePath(Path.GetFullPath(root), source.Path) : source.Path;
        var builder = this.buffer.Clear();
        Span<char> hex = stackalloc char[8];
        foreach (var rune in path.EnumerateRunes())
        {
            if (rune.Value == Path.DirectorySeparatorChar)
            {
                builder.Append('/');
            }
            else if (rune.Value == '\\')
            {
                builder.Append("\\\\");
            }
            else if (rune.Value < 32 || rune.Value >= 127)
            {
                rune.Value.TryFormat(hex, out var count, "X", CultureInfo.InvariantCulture);
                builder.Append("\\u{").Append(hex[..count]).Append('}');
            }
            else
            {
                builder.Append((char)rune.Value);
            }
        }

        var position = source.GetPosition(node.Span.Start);
        text = builder.Append(':').Append(position.Line + 1).Append(':').Append(position.Character + 1).ToString();
        this.texts.Add(key, text);
        return text;
    }

    /// <summary>Forgets every text when the source analysis is revoked, so the memo holds only the current program's documents.</summary>
    internal void Clear() => this.texts.Clear();
}
