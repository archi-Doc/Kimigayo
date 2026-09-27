// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using Kimi.Compiler;
using Kimi.Diagnostics;

namespace Kimi.Lsp;

/// <summary>
/// Holds the text of one open document in a single pooled buffer (SPEC 23.4.2).
/// This type is not thread-safe: only the state owner touches it.
/// </summary>
/// <remarks>
/// Line starts follow the <see cref="SourceDocument"/> rule, so editor positions and diagnostics agree on CR, LF and
/// CRLF, including a CR and an LF joined by an edit. Positions are UTF-16 code units.
/// </remarks>
internal sealed class TextDocument : IDisposable
{
    private static readonly ArrayPool<char> Pool = ArrayPool<char>.Shared;

    private char[] buffer = [];
    private int length;
    private int[] lineStarts = new int[16];
    private int lineCount = 1;
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
    public int LineCount => this.lineCount;

    /// <summary>Gets the current text.</summary>
    public ReadOnlySpan<char> Span => this.buffer.AsSpan(0, this.length);

    /// <summary>Replaces the whole text.</summary>
    /// <param name="value">The new text.</param>
    public void Replace(string value)
    {
        this.EnsureCapacity(value.Length, false);
        value.AsSpan().CopyTo(this.buffer);
        this.length = value.Length;
        this.text = value;
        this.RebuildLines();
    }

    /// <summary>Applies one ranged change. A character beyond its line end is clamped, as LSP specifies.</summary>
    /// <param name="start">The start position.</param>
    /// <param name="end">The end position.</param>
    /// <param name="replacement">The inserted text.</param>
    /// <returns><see langword="false"/> when the change cannot be applied: a reversed range, or a line beyond the document.</returns>
    public bool TryApply(SourcePosition start, SourcePosition end, ReadOnlySpan<char> replacement)
    {
        if (!this.TryGetOffset(start, out var from) || !this.TryGetOffset(end, out var to) || to < from)
        {
            return false;
        }

        var removed = to - from;
        var newLength = this.length - removed + replacement.Length;
        this.EnsureCapacity(newLength, true);
        var tail = this.length - to;
        if (replacement.Length != removed && tail > 0)
        {
            this.buffer.AsSpan(to, tail).CopyTo(this.buffer.AsSpan(from + replacement.Length));
        }

        replacement.CopyTo(this.buffer.AsSpan(from));
        this.length = newLength;
        this.text = null;
        this.RebuildLines();
        return true;
    }

    /// <summary>Returns the text as one string, created at most once per change.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
        => this.text ??= new string(this.buffer, 0, this.length);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (this.buffer.Length != 0)
        {
            Pool.Return(this.buffer);
            this.buffer = [];
        }

        this.length = 0;
        this.text = null;
    }

    private bool TryGetOffset(SourcePosition position, out int offset)
    {
        offset = 0;
        if (position.Line < 0 || position.Character < 0 || position.Line >= this.lineCount)
        {
            return false;
        }

        var lineStart = this.lineStarts[position.Line];
        var lineEnd = position.Line + 1 < this.lineCount ? this.lineStarts[position.Line + 1] : this.length;
        var span = this.buffer.AsSpan(0, this.length);
        if (lineEnd > lineStart && span[lineEnd - 1] == Constants.LfChar)
        {
            lineEnd--;
        }

        if (lineEnd > lineStart && span[lineEnd - 1] == Constants.CrChar)
        {
            lineEnd--;
        }

        offset = lineStart + Math.Min(position.Character, lineEnd - lineStart);
        return true;
    }

    private void EnsureCapacity(int capacity, bool preserve)
    {
        if (capacity <= this.buffer.Length && this.buffer.Length != 0)
        {
            return;
        }

        var larger = Pool.Rent(Math.Max(capacity, Math.Max(256, this.buffer.Length * 2)));
        if (preserve)
        {
            this.buffer.AsSpan(0, this.length).CopyTo(larger);
        }

        if (this.buffer.Length != 0)
        {
            Pool.Return(this.buffer);
        }

        this.buffer = larger;
    }

    private void RebuildLines()
        => this.lineCount = SourceDocument.FillLineStarts(this.buffer.AsSpan(0, this.length), ref this.lineStarts, null);
}
