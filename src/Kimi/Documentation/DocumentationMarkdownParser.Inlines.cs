// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Kimi.Documentation;

#pragma warning disable SA1201, SA1202, SA1204, SA1401, SA1513, SA1600 // Delimiter algorithm and compact scratch records.

// The inline pass over one paragraph or heading: the block's input, the scan between marker characters, and text nodes. Each
// construct a marker can start is parsed by its own member (`.CodeSpans`, `.Emphasis`, `.Escapes`, `.Links`, `.AutoLinks`), which
// returns the position after the construct, or the marker's own position when the input there is not that construct: the marker
// is then literal text. An unmatched backtick string is literal as a whole, so ParseCodeSpan adds it itself and returns its end
// (returning the marker's position would rescan the rest of the string as a shorter run).
internal ref partial struct DocumentationMarkdownParser
{
    // A NUL in the needle selects a slower vectorized searcher, so NUL-free input
    // (the norm) uses a set without it.
    private static readonly SearchValues<char> InlineCharacters = SearchValues.Create("*_`[\\&\n<]");

    private static readonly SearchValues<char> InlineCharactersWithNul = SearchValues.Create("*_`[\\&\n\0<]");

    private void ParseInlines(int parent)
    {
        var first = this.nodes[parent].TextStart;
        var count = this.nodes[parent].TextLength;
        this.nodes[parent].TextStart = 0;
        this.nodes[parent].TextLength = 0;
        if (count == 0)
        {
            return;
        }

        this.inlineDepth = this.links[parent].Level;
        this.lastPlainText = 0;
        var input = this.ReadInlineInput(first, count);
        this.delimiters.Clear();
        this.brackets.Clear();
        this.codeRuns.Clear();
        this.indexedParentheses = false;
        this.stopSearchedFrom = int.MaxValue;
        this.stopFound = -1;
        this.inlineHasNul = this.hasNul && input.Contains('\0');
        this.inlineMultiline = count > 1;
        var markers = this.inlineHasNul ? InlineCharactersWithNul : InlineCharacters;
        // Absolute index of the next marker at or after position; -1 once none remain.
        var next = input.IndexOfAny(markers);
        if (next < 0)
        {
            if (!input.IsEmpty)
            {
                this.AddPlainText(parent, input, 0, input.Length);
                this.maxDepth = Math.Max(this.maxDepth, this.inlineDepth + 1);
            }

            return;
        }

        this.delimiters.Add(default);
        this.brackets.Add(default);
        this.lastDelimiter = this.lastBracket = 0;
        this.indexedCode = false;
        this.lastLinkStart = -1;
        ((Span<int>)this.failedTitles).Fill(int.MaxValue);
        var position = 0;
        var tokens = 0;
        while (position < input.Length)
        {
            if ((++tokens & 255) == 0)
            {
                this.cancellationToken.ThrowIfCancellationRequested();
            }

            if (next >= 0 && next < position)
            {
                var delta = input[position..].IndexOfAny(markers);
                next = delta < 0 ? -1 : position + delta;
            }

            var plain = next < 0 ? input.Length - position : next - position;
            if (plain > 0)
            {
                var end = position + plain;
                var textEnd = end;
                if (end < input.Length && input[end] == '\n')
                {
                    while (textEnd > position && input[textEnd - 1] is ' ' or '\t')
                    {
                        textEnd--;
                    }
                }

                if (textEnd > position)
                {
                    this.AddPlainText(parent, input, position, textEnd);
                }

                position = end;
                continue;
            }

            var after = input[position] switch
            {
                '\n' => this.AddLineEnding(parent, input, position),
                '\0' => this.AddDecoded(parent, position, position + 1, 0xFFFD),
                '\\' => this.ParseEscape(parent, input, position),
                '&' => this.ParseCharacterReference(parent, input, position),
                '`' => this.ParseCodeSpan(parent, input, position),
                '*' or '_' => this.AddDelimiterRun(parent, input, position),
                '[' => this.OpenBracket(parent, input, position),
                ']' => this.CloseBracket(parent, input, position),
                '<' => this.ParseAutoLink(parent, input, position),
                _ => position,
            };
            if (after == position)
            {
                this.AddPlainText(parent, input, position, position + 1);
                after++;
            }

            position = after;
        }

        this.ProcessEmphasis(0);
        this.maxDepth = Math.Max(this.maxDepth, this.inlineDepth + 1);
    }

    // The block's content lines as one input: the source slice when no prefix was removed between them, else joined with LF into
    // the pooled buffer with the line map MapStart reads. Trailing spaces and tabs end no content.
    private ReadOnlySpan<char> ReadInlineInput(int first, int count)
    {
        this.inlineBase = this.lines[first].Start;
        this.mappedCount = 0;
        var last = first + count - 1;
        var contiguous = true;
        for (var line = first + 1; line <= last; line++)
        {
            contiguous &= this.lines[line].Start == this.lines[line - 1].End + 1;
        }

        if (contiguous)
        {
            // Continuation lines without removed prefixes are already one slice.
            return this.text.AsSpan(this.inlineBase, this.lines[last].End - this.inlineBase).TrimEnd(" \t");
        }

        var length = count - 1;
        for (var line = first; line <= last; line++)
        {
            length += this.lines[line].End - this.lines[line].Start;
        }

        EnsureBuffer(ref this.inlineCharacters, length);
        var written = 0;
        for (var line = first; line <= last; line++)
        {
            ref var slice = ref this.lines[line];
            if (line > first)
            {
                this.inlineCharacters![written++] = '\n';
            }

            slice.Joined = written;
            var sliceLength = slice.End - slice.Start;
            this.text.AsSpan(slice.Start, sliceLength).CopyTo(this.inlineCharacters.AsSpan(written));
            written += sliceLength;
        }

        this.mappedFirst = first;
        this.mappedCount = count;
        return this.inlineCharacters.AsSpan(0, written).TrimEnd(" \t");
    }

    // A line ending is a hard break after two or more spaces, which it then spans, else a soft break.
    private int AddLineEnding(int parent, ReadOnlySpan<char> input, int position)
    {
        var spaces = position;
        while (spaces > 0 && input[spaces - 1] == ' ')
        {
            spaces--;
        }

        var hard = position - spaces >= 2;
        this.AddNode(
            hard ? DocumentationMarkdownKind.HardBreak : DocumentationMarkdownKind.SoftBreak,
            parent,
            this.MapStart(hard ? spaces : position),
            this.MapEnd(position + 1));
        return position + 1;
    }

    // An inline wrapper (a link or emphasis) adopts the sibling chain from first. Its leaves are at (block depth + height + 1),
    // where its height is its tallest child's plus one (0 without children). Fail early.
    private void Adopt(int wrapper, int first)
    {
        var height = 0;
        for (var child = first; child != 0; child = this.nodes[child].Next)
        {
            this.nodes[child].Parent = wrapper;
            height = Math.Max(height, this.links[child].Level + 1);
        }

        this.links[wrapper].Level = height;
        var depth = this.inlineDepth + height + 1;
        if (depth > this.maximumDepth)
        {
            throw new DocumentationMarkdownLimitException(this.maximumDepth);
        }

        this.maxDepth = Math.Max(this.maxDepth, depth);
    }

    private int AddInlineText(int parent, ReadOnlySpan<char> input, int start, int end)
    {
        var sourceStart = this.MapStart(start);
        var sourceEnd = this.MapEnd(end);
        var id = this.AddNode(DocumentationMarkdownKind.Text, parent, sourceStart, sourceEnd);
        this.SetInlineText(id, input, start, end, sourceStart, sourceEnd);
        return id;
    }

    // Plain runs never become delimiters or links, so a run that continues the
    // previous plain node's source slice extends that node instead of adding one.
    private void AddPlainText(int parent, ReadOnlySpan<char> input, int start, int end)
    {
        var sourceStart = this.MapStart(start);
        var sourceEnd = this.MapEnd(end);
        var last = this.lastPlainText;
        if (last != 0 && this.nodes[last].End == sourceStart && sourceEnd - sourceStart == end - start)
        {
            this.nodes[last].End = sourceEnd;
            this.nodes[last].TextLength += end - start;
            return;
        }

        var id = this.AddNode(DocumentationMarkdownKind.Text, parent, sourceStart, sourceEnd);
        this.SetInlineText(id, input, start, end, sourceStart, sourceEnd);
        this.lastPlainText = this.nodes[id].TextStart >= 0 ? id : 0;
    }

    private int AddDecoded(int parent, int start, int end, int scalar)
    {
        var id = this.AddNode(DocumentationMarkdownKind.Text, parent, this.MapStart(start), this.MapEnd(end));
        var first = scalar <= 0xFFFF ? scalar : 0xD800 + ((scalar - 0x10000) >> 10);
        var second = scalar <= 0xFFFF ? 0 : 0xDC00 + ((scalar - 0x10000) & 1023);
        this.nodes[id].TextStart = int.MinValue;
        this.nodes[id].TextLength = second == 0 ? 1 : 2;
        this.nodes[id].Argument = BitConverter.IsLittleEndian ? first | (second << 16) : (first << 16) | second;
        return end;
    }

    // Text holding no NUL or line ending (other runs stop at both; code spans normalize them first) borrows its source slice
    // unless a joined line boundary falls inside it.
    private void SetInlineText(int id, ReadOnlySpan<char> input, int start, int end, int sourceStart, int sourceEnd)
    {
        if (sourceEnd - sourceStart == end - start)
        {
            this.nodes[id].TextStart = sourceStart;
            this.nodes[id].TextLength = end - start;
        }
        else
        {
            this.SetValue(id, this.CreateText(input[start..end], normalizeCode: false));
        }
    }

    // One allocation: NULs become U+FFFD and, inside code, line endings become spaces.
    private string CreateText(ReadOnlySpan<char> value, bool normalizeCode)
    {
        EnsureBuffer(ref this.decodeScratch, value.Length);
        var output = this.decodeScratch.AsSpan(0, value.Length);
        value.CopyTo(output);
        output.Replace('\0', '\uFFFD');
        if (normalizeCode)
        {
            output.Replace('\n', ' ');
        }

        return new string(output);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int MapStart(int position) => this.mappedCount == 0 ? this.inlineBase + position : this.MapJoined(position);

    private int MapJoined(int position)
    {
        var low = this.mappedFirst;
        var high = low + this.mappedCount - 1;
        while (low < high)
        {
            var middle = (low + high + 1) >> 1;
            if (this.lines[middle].Joined <= position)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        ref var line = ref this.lines[low];
        return line.Start + (position - line.Joined);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int MapEnd(int end) => end == 0 ? this.inlineBase : this.MapStart(end - 1) + 1;

    private MarkdownValue SliceValue(ReadOnlySpan<char> input, int start, int end)
    {
        if (end == start)
        {
            return new(string.Empty);
        }

        var sourceStart = this.MapStart(start);
        return this.MapEnd(end) - sourceStart == end - start ? new(sourceStart, end - start) : new(input[start..end].ToString());
    }

    private static void EnsureBuffer<T>(ref T[]? buffer, int length)
    {
        if (buffer is not null && buffer.Length >= length)
        {
            return;
        }

        if (buffer is not null)
        {
            ArrayPool<T>.Shared.Return(buffer);
        }

        buffer = ArrayPool<T>.Shared.Rent(length);
    }

    private static void ReturnBuffer<T>(ref T[]? buffer)
    {
        if (buffer is not null)
        {
            ArrayPool<T>.Shared.Return(buffer);
            buffer = null;
        }
    }

    private void DisposeInlineBuffers()
    {
        this.delimiters.Dispose();
        this.brackets.Dispose();
        this.codeRuns.Dispose();
        ReturnBuffer(ref this.codeHeads);
        ReturnBuffer(ref this.inlineCharacters);
        ReturnBuffer(ref this.parentheses);
        ReturnBuffer(ref this.decodeScratch);
    }
}
