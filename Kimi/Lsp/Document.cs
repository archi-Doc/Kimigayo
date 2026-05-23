// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Lsp;

internal sealed class Document : IDisposable
{
    #region FieldAndProperty

    private readonly List<Line> lines = new();
    private bool disposed;

    public string Uri { get; }

    public int Version { get; private set; }

    public IReadOnlyList<Line> Lines => this.lines;

    #endregion

    public Document(string uri)
    {
        this.Uri = uri;
    }

    public void Open(string text, int version)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        this.ClearLines();

        AddLines(this.lines, text.AsSpan());

        this.Version = version;
    }

    public void ApplyChange(int startLine, int startCharacter, int endLine, int endCharacter, string text, int version)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        if (this.lines.Count == 0)
        {
            this.lines.Add(new Line());
        }

        startLine = Math.Clamp(startLine, 0, this.lines.Count - 1);
        endLine = Math.Clamp(endLine, startLine, this.lines.Count - 1);

        var start = this.lines[startLine];
        var end = this.lines[endLine];

        startCharacter = Math.Clamp(startCharacter, 0, start.Length);
        endCharacter = Math.Clamp(endCharacter, 0, end.Length);

        var prefix = start.AsSpan()[..startCharacter];
        var suffix = end.AsSpan()[endCharacter..];
        var replacement = text.AsSpan();

        var firstBreak = IndexOfLineBreak(replacement, out var firstBreakLength);

        if (firstBreak < 0)
        {
            start.Replace(startCharacter, endCharacter, replacement);

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
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.ClearLines();
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
}
