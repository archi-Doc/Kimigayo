// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Kimigayo.Lsp;

/// <summary>
/// Represents an LSP text document as pooled text lines.
/// This type is not thread-safe.
/// </summary>
internal sealed class Document : IDisposable
{
    #region FieldAndProperty

    private readonly List<Line> lines = new();
    private readonly List<Line> workLines = new();
    private bool disposed;

    public string Uri { get; }

    public int Version { get; private set; }

    public IReadOnlyList<Line> Lines => this.lines;

    #endregion

    public Document(string uri)
    {
        this.Uri = uri ?? throw new ArgumentNullException(nameof(uri));
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

        var replacement = text.AsSpan();
        var firstBreak = IndexOfLineBreak(replacement, out var firstBreakLength);

        // Fast path: same-line replacement without line breaks.
        if (firstBreak < 0 && startLine == endLine)
        {
            start.Replace(startCharacter, endCharacter, replacement);
            this.Version = version;
            return;
        }

        var prefix = start.AsSpan()[..startCharacter];
        var suffix = end.AsSpan()[endCharacter..];

        var newLines = this.workLines;
        newLines.Clear();

        try
        {
            if (firstBreak < 0)
            {
                var line = new Line();
                line.Set(prefix, replacement, suffix);
                newLines.Add(line);
            }
            else
            {
                var firstLine = new Line();
                firstLine.Set(prefix, replacement[..firstBreak]);
                newLines.Add(firstLine);

                var position = firstBreak + firstBreakLength;

                while (true)
                {
                    var rest = replacement[position..];
                    var nextBreak = IndexOfLineBreak(rest, out var nextBreakLength);

                    if (nextBreak < 0)
                    {
                        var lastLine = new Line();
                        lastLine.Set(rest, suffix);
                        newLines.Add(lastLine);
                        break;
                    }

                    var line = new Line();
                    line.Set(rest[..nextBreak]);
                    newLines.Add(line);

                    position += nextBreak + nextBreakLength;
                }
            }

            this.ReplaceLines(startLine, endLine - startLine + 1, newLines);
            this.Version = version;
        }
        catch
        {
            DisposeLines(newLines);
            newLines.Clear();
            throw;
        }
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        this.ClearLines();
        DisposeLines(this.workLines);
        this.workLines.Clear();
    }

    private static void DisposeLines(List<Line> lines)
    {
        foreach (var line in lines)
        {
            line.Dispose();
        }
    }

    private static void AddLines(List<Line> lines, ReadOnlySpan<char> text)
    {
        var start = 0;

        for (var i = 0; i < text.Length;)
        {
            var next = text[i..].IndexOfAny('\r', '\n');

            if (next < 0)
            {
                break;
            }

            var lineEnd = i + next;

            var line = new Line();
            line.Set(text[start..lineEnd]);
            lines.Add(line);

            i = lineEnd;
            i += text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;

            start = i;
        }

        var lastLine = new Line();
        lastLine.Set(text[start..]);
        lines.Add(lastLine);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int IndexOfLineBreak(ReadOnlySpan<char> text, out int lineBreakLength)
    {
        var index = text.IndexOfAny('\r', '\n');

        if (index < 0)
        {
            lineBreakLength = 0;
            return -1;
        }

        lineBreakLength = (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n') ? 2 : 1;

        return index;
    }

    private void ReplaceLines(int index, int count, List<Line> newLines)
    {
        Debug.Assert(index >= 0);
        Debug.Assert(count >= 0);
        Debug.Assert(index + count <= this.lines.Count);

        var newCount = newLines.Count;

        if (newCount == count)
        {
            for (var i = 0; i < count; i++)
            {
                this.lines[index + i].Dispose();
                this.lines[index + i] = newLines[i];
            }

            newLines.Clear();
            return;
        }

        this.lines.EnsureCapacity(this.lines.Count - count + newCount);

        for (var i = index; i < index + count; i++)
        {
            this.lines[i].Dispose();
        }

        this.lines.RemoveRange(index, count);
        this.lines.InsertRange(index, newLines);

        // Ownership has moved to this.lines.
        newLines.Clear();
    }

    private void ClearLines()
    {
        DisposeLines(this.lines);
        this.lines.Clear();
    }
}
