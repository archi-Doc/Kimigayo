// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;

namespace Kimigayo.Lsp;

internal sealed record TextChange(int StartLine, int StartCharacter, int EndLine, int EndCharacter, string Text);

internal sealed record TomlDiagnostic(int Line, int Character, int Length, string Severity, string Message);

internal sealed class TomlDocumentState : IDisposable
{
    private readonly List<Line> lines = new();

    public TomlDocumentState(string uri)
    {
        this.Uri = uri;
    }

    public string Uri { get; }

    public int Version { get; private set; }

    public IReadOnlyList<Line> Lines => this.lines;

    public void Open(string text, int version)
    {
        this.ClearLines();

        AddLines(this.lines, text.AsSpan());

        this.Version = version;
    }

    public void ApplyChange(TextChange change, int version)
    {
        if (this.lines.Count == 0)
        {
            this.lines.Add(new Line());
        }

        var startLine = Math.Clamp(change.StartLine, 0, this.lines.Count - 1);
        var endLine = Math.Clamp(change.EndLine, startLine, this.lines.Count - 1);

        var start = this.lines[startLine];
        var end = this.lines[endLine];

        var startCharacter = Math.Clamp(change.StartCharacter, 0, start.Length);
        var endCharacter = Math.Clamp(change.EndCharacter, 0, end.Length);

        var prefix = start.AsSpan()[..startCharacter];
        var suffix = end.AsSpan()[endCharacter..];
        var replacement = change.Text.AsSpan();

        var firstBreak = IndexOfLineBreak(replacement, out var firstBreakLength);

        if (firstBreak < 0)
        {
            start.Set(prefix, replacement, suffix);

            if (endLine > startLine)
            {
                this.RemoveLines(startLine + 1, endLine - startLine);
            }

            this.Version = version;
            return;
        }

        // First replacement line.
        start.Set(prefix, replacement[..firstBreak]);

        if (endLine > startLine)
        {
            this.RemoveLines(startLine + 1, endLine - startLine);
        }

        var insertIndex = startLine + 1;
        var position = firstBreak + firstBreakLength;

        while (true)
        {
            var rest = replacement[position..];
            var nextBreak = IndexOfLineBreak(rest, out var nextBreakLength);

            if (nextBreak < 0)
            {
                var line = new Line();
                line.Set(rest, suffix);
                this.lines.Insert(insertIndex, line);
                break;
            }

            {
                var line = new Line();
                line.Set(rest[..nextBreak]);
                this.lines.Insert(insertIndex++, line);
            }

            position += nextBreak + nextBreakLength;
        }

        this.Version = version;
    }

    public void Dispose()
    {
        this.ClearLines();
    }

    private void ClearLines()
    {
        foreach (var line in this.lines)
        {
            line.Dispose();
        }

        this.lines.Clear();
    }

    private void RemoveLines(int index, int count)
    {
        for (var i = index; i < index + count; i++)
        {
            this.lines[i].Dispose();
        }

        this.lines.RemoveRange(index, count);
    }

    private static void AddLines(List<Line> lines, ReadOnlySpan<char> text)
    {
        var start = 0;
        var index = 0;

        while (index < text.Length)
        {
            var c = text[index];

            if (c == '\r' || c == '\n')
            {
                var line = new Line();
                line.Set(text[start..index]);
                lines.Add(line);

                if (c == '\r' &&
                    index + 1 < text.Length &&
                    text[index + 1] == '\n')
                {
                    index += 2;
                }
                else
                {
                    index++;
                }

                start = index;
                continue;
            }

            index++;
        }

        // Keep the last line, including the final empty line.
        {
            var line = new Line();
            line.Set(text[start..]);
            lines.Add(line);
        }
    }

    private static int IndexOfLineBreak(ReadOnlySpan<char> text, out int lineBreakLength)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n')
            {
                lineBreakLength = 1;
                return i;
            }

            if (c == '\r')
            {
                lineBreakLength = i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                return i;
            }
        }

        lineBreakLength = 0;
        return -1;
    }
}
