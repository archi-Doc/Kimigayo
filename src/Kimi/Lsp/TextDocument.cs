// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using Kimi.Compiler;
using Kimi.Diagnostics;

namespace Kimi.Lsp;

/// <summary>
/// Holds the text of one open document in a pooled gap buffer (SPEC 23.4.2).
/// This type is not thread-safe: only the state owner touches it.
/// </summary>
/// <remarks>
/// Line starts follow the <see cref="SourceDocument"/> rule, so editor positions and diagnostics agree on CR, LF and
/// CRLF, including a CR and an LF joined by an edit. Positions are UTF-16 code units.
/// The text and its line starts each keep a gap at the last edit, so an edit costs its own size plus its distance from
/// the previous edit, not the length of the document.
/// </remarks>
internal sealed class TextDocument : IDisposable
{
    private static readonly ArrayPool<char> Pool = ArrayPool<char>.Shared;

    // The text is buffer[..gapStart] followed by buffer[gapEnd..].
    private char[] buffer = [];
    private int gapStart;
    private int gapEnd;
    private int length;

    // The entries before lineGapStart are offsets. The entries from lineGapEnd are distances from the end of the text,
    // which an edit before them keeps, so the lines after an edit are never shifted.
    private int[] lineStarts = new int[16];
    private int lineGapStart = 1;
    private int lineGapEnd = 16;
    private string? text;

    /// <summary>Initializes a new instance of the <see cref="TextDocument"/> class.</summary>
    /// <param name="text">The initial text.</param>
    public TextDocument(string text)
    {
        this.Replace(text);
    }

    /// <summary>Gets the number of UTF-16 code units.</summary>
    public int Length => this.length;

    /// <summary>Gets the number of lines.</summary>
    public int LineCount => this.lineGapStart + this.lineStarts.Length - this.lineGapEnd;

    /// <summary>Gets the offset at which each line starts; reading it moves the line gap to the end.</summary>
    public ReadOnlySpan<int> LineStarts
    {
        get
        {
            this.MoveLineGap(this.LineCount);
            return this.lineStarts.AsSpan(0, this.lineGapStart);
        }
    }

    /// <summary>Replaces the whole text.</summary>
    /// <param name="value">The new text.</param>
    public void Replace(string value)
    {
        if (value.Length == this.length && this.TextEquals(0, value))
        {
            this.text = value;
            return;
        }

        if (value.Length > this.buffer.Length || this.buffer.Length == 0)
        {
            this.ReturnBuffer();
            this.buffer = Pool.Rent(Math.Max(value.Length, 256));
        }

        value.CopyTo(this.buffer);
        this.length = this.gapStart = value.Length;
        this.gapEnd = this.buffer.Length;
        this.text = value;
        this.lineGapStart = SourceDocument.FillLineStarts(value, ref this.lineStarts, null);
        this.lineGapEnd = this.lineStarts.Length;
    }

    /// <summary>Applies one ranged change. A character beyond its line end is clamped, as LSP specifies.</summary>
    /// <param name="start">The start position.</param>
    /// <param name="end">The end position.</param>
    /// <param name="replacement">The inserted text.</param>
    /// <returns><see langword="false"/> when the change cannot be applied: a reversed range, or a line beyond the document.</returns>
    public bool TryApply(SourcePosition start, SourcePosition end, ReadOnlySpan<char> replacement)
    {
        if (start.CompareTo(end) > 0 || !this.TryGetOffset(start, out var from) || !this.TryGetOffset(end, out var to) || to < from)
        {
            return false;
        }

        var removed = to - from;
        if (removed == replacement.Length && this.TextEquals(from, replacement))
        {
            return true;
        }

        // A start depends only on the character before it and the one at it, so the starts before max(from, 1) stay,
        // the starts after to move with the text, and only the positions from max(from, 1) to from + inserted change.
        // The clamped offsets lie within their lines, so those starts are the lines up to start.Line and after end.Line.
        var scanStart = Math.Max(from, 1);
        var kept = start.Line + (this.GetLineStart(start.Line) < scanStart ? 1 : 0);
        var suffix = end.Line + 1;
        if (this.lineGapStart < kept)
        {
            this.MoveLineGap(kept);
        }
        else if (this.lineGapStart > suffix)
        {
            this.MoveLineGap(suffix);
        }

        this.lineGapEnd += suffix - this.lineGapStart;
        this.lineGapStart = kept;

        this.MoveGap(from);
        this.gapEnd += removed;
        if (this.gapEnd - this.gapStart < replacement.Length)
        {
            this.GrowGap(replacement.Length);
        }

        replacement.CopyTo(this.buffer.AsSpan(this.gapStart));
        this.gapStart += replacement.Length;
        this.length += replacement.Length - removed;
        this.text = null;
        this.AddLineStarts(scanStart, this.gapStart);
        return true;
    }

    /// <summary>Returns the text as one string, created at most once per change.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
        => this.text ??= string.Create(this.length, this, static (destination, document) =>
        {
            document.buffer.AsSpan(0, document.gapStart).CopyTo(destination);
            document.buffer.AsSpan(document.gapEnd).CopyTo(destination[document.gapStart..]);
        });

    /// <inheritdoc/>
    public void Dispose()
    {
        this.ReturnBuffer();
        this.length = this.gapStart = this.gapEnd = 0;
        this.text = null;
    }

    private int GetLineStart(int line)
        => line < this.lineGapStart ? this.lineStarts[line] : this.length - this.lineStarts[line - this.lineGapStart + this.lineGapEnd];

    private char CharAt(int index)
        => this.buffer[index < this.gapStart ? index : index + this.gapEnd - this.gapStart];

    // Compares the text from an offset with a value that ends within the text.
    private bool TextEquals(int from, ReadOnlySpan<char> value)
    {
        var split = Math.Clamp(this.gapStart - from, 0, value.Length);
        return value[..split].SequenceEqual(this.buffer.AsSpan(from, split)) &&
            value[split..].SequenceEqual(this.buffer.AsSpan(from + split + this.gapEnd - this.gapStart, value.Length - split));
    }

    private bool TryGetOffset(SourcePosition position, out int offset)
    {
        offset = 0;
        var lineCount = this.LineCount;
        if (position.Line < 0 || position.Character < 0 || position.Line >= lineCount)
        {
            return false;
        }

        var lineStart = this.GetLineStart(position.Line);
        var lineEnd = position.Line + 1 < lineCount ? this.GetLineStart(position.Line + 1) : this.length;
        if (lineEnd > lineStart && this.CharAt(lineEnd - 1) == Constants.LfChar)
        {
            lineEnd--;
        }

        if (lineEnd > lineStart && this.CharAt(lineEnd - 1) == Constants.CrChar)
        {
            lineEnd--;
        }

        offset = lineStart + Math.Min(position.Character, lineEnd - lineStart);
        return true;
    }

    // Moves the text gap to an offset; only the characters between the old and new place move.
    private void MoveGap(int position)
    {
        if (position < this.gapStart)
        {
            var count = this.gapStart - position;
            this.buffer.AsSpan(position, count).CopyTo(this.buffer.AsSpan(this.gapEnd - count));
            this.gapStart = position;
            this.gapEnd -= count;
        }
        else if (position > this.gapStart)
        {
            var count = position - this.gapStart;
            this.buffer.AsSpan(this.gapEnd, count).CopyTo(this.buffer.AsSpan(this.gapStart));
            this.gapStart = position;
            this.gapEnd += count;
        }
    }

    private void GrowGap(int required)
    {
        var after = this.buffer.Length - this.gapEnd;
        var larger = Pool.Rent(Math.Max(this.length + required, Math.Max(256, this.buffer.Length * 2)));
        this.buffer.AsSpan(0, this.gapStart).CopyTo(larger);
        this.buffer.AsSpan(this.gapEnd, after).CopyTo(larger.AsSpan(larger.Length - after));
        this.ReturnBuffer();
        this.buffer = larger;
        this.gapEnd = larger.Length - after;
    }

    // Moves the line gap to a line index, converting the entries that cross it; the text length must be current.
    private void MoveLineGap(int index)
    {
        var starts = this.lineStarts;
        while (this.lineGapStart > index)
        {
            starts[--this.lineGapEnd] = this.length - starts[--this.lineGapStart];
        }

        while (this.lineGapStart < index)
        {
            starts[this.lineGapStart++] = this.length - starts[this.lineGapEnd++];
        }
    }

    // Adds the starts at the positions from scanStart to scanEnd, where the inserted text and the text gap end.
    // A start is a position after an LF, or after a CR that no LF follows.
    private void AddLineStarts(int scanStart, int scanEnd)
    {
        if (scanStart > scanEnd)
        {
            return;
        }

        var previous = this.buffer.AsSpan(scanStart - 1, scanEnd - scanStart + 1);
        var last = scanEnd < this.length ? this.buffer[this.gapEnd] : '\0';
        for (var index = previous.IndexOfAny(Constants.CrChar, Constants.LfChar); index >= 0;)
        {
            var next = index + 1 < previous.Length ? previous[index + 1] : last;
            if (previous[index] == Constants.LfChar || next != Constants.LfChar)
            {
                if (this.lineGapStart == this.lineGapEnd)
                {
                    this.GrowLines();
                }

                this.lineStarts[this.lineGapStart++] = scanStart + index;
            }

            var following = previous[(index + 1)..].IndexOfAny(Constants.CrChar, Constants.LfChar);
            index = following < 0 ? -1 : index + 1 + following;
        }
    }

    private void GrowLines()
    {
        var after = this.lineStarts.Length - this.lineGapEnd;
        var larger = new int[this.lineStarts.Length * 2];
        this.lineStarts.AsSpan(0, this.lineGapStart).CopyTo(larger);
        this.lineStarts.AsSpan(this.lineGapEnd).CopyTo(larger.AsSpan(larger.Length - after));
        this.lineStarts = larger;
        this.lineGapEnd = larger.Length - after;
    }

    private void ReturnBuffer()
    {
        if (this.buffer.Length != 0)
        {
            Pool.Return(this.buffer);
            this.buffer = [];
        }
    }
}
