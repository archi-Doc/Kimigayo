// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;

namespace Kimigayo.Lsp;

internal sealed record TextChange(int StartLine, int StartCharacter, int EndLine, int EndCharacter, string Text);

internal sealed record TomlDiagnostic(int Line, int Character, int Length, string Severity, string Message);

internal sealed class TomlDocumentState
{
    private readonly List<string> lines = new();

    public TomlDocumentState(string uri)
    {
        this.Uri = uri;
    }

    public string Uri { get; }

    public int Version { get; private set; }

    public IReadOnlyList<string> Lines => this.lines;

    public void Open(string text, int version)
    {
        this.lines.Clear();
        this.lines.AddRange(SplitLines(text));
        this.Version = version;
    }

    public void ApplyChange(TextChange change, int version)
    {
        var aa = ArrayPool<char>.Shared.Rent();
        if (this.lines.Count == 0)
        {
            this.lines.Add(string.Empty);
        }

        var startLine = Math.Min(change.StartLine, this.lines.Count - 1);
        var endLine = Math.Min(change.EndLine, this.lines.Count - 1);

        var startCharacter = Math.Min(change.StartCharacter, this.lines[startLine].Length);
        var endCharacter = Math.Min(change.EndCharacter, this.lines[endLine].Length);

        var prefix = this.lines[startLine][..startCharacter];
        var suffix = this.lines[endLine][endCharacter..];

        var replacementLines = SplitLines(change.Text);

        if (replacementLines.Length == 1)
        {
            this.lines[startLine] = prefix + replacementLines[0] + suffix;

            if (endLine > startLine)
            {
                this.lines.RemoveRange(startLine + 1, endLine - startLine);
            }
        }
        else
        {
            replacementLines[0] = prefix + replacementLines[0];
            replacementLines[^1] += suffix;

            this.lines[startLine] = replacementLines[0];

            if (endLine > startLine)
            {
                this.lines.RemoveRange(startLine + 1, endLine - startLine);
            }

            this.lines.InsertRange(startLine + 1, replacementLines.AsSpan(1).ToArray());
        }

        this.Version = version;
    }

    private static string[] SplitLines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
}
