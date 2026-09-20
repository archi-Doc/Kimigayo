// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Documentation;

#pragma warning disable SA1201, SA1202, SA1204, SA1401, SA1513, SA1600 // Delimiter algorithm and compact scratch records.

internal ref partial struct DocumentationMarkdownParser
{
    // A NUL in the needle selects a slower vectorized searcher, so NUL-free input
    // (the norm) uses a set without it.
    private static readonly SearchValues<char> InlineCharacters = SearchValues.Create("*_`[\\&\n<]");

    private static readonly SearchValues<char> InlineCharactersWithNul = SearchValues.Create("*_`[\\&\n\0<]");

    private static readonly SearchValues<char> DecodeCharacters = SearchValues.Create("\\&\0");

    private static readonly SearchValues<char> ParenthesisCharacters = SearchValues.Create("()\\");

    // NUL is logically U+FFFD, not a destination-ending control character.
    private static readonly SearchValues<char> DestinationStops = SearchValues.Create("\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\t\n\u000B\u000C\r\u000E\u000F\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F \u007F");

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
        this.inlineBase = this.lines[first].Start;
        this.mappedCount = 0;
        this.lastPlainText = 0;
        var last = first + count - 1;
        var contiguous = true;
        for (var line = first + 1; line <= last; line++)
        {
            contiguous &= this.lines[line].Start == this.lines[line - 1].End + 1;
        }

        ReadOnlySpan<char> input;
        if (contiguous)
        {
            // Continuation lines without removed prefixes are already one slice.
            input = this.text.AsSpan(this.inlineBase, this.lines[last].End - this.inlineBase).TrimEnd(" \t");
        }
        else
        {
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

            input = this.inlineCharacters.AsSpan(0, written).TrimEnd(" \t");
            this.mappedFirst = first;
            this.mappedCount = count;
        }

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
        var indexedCode = false;
        var lastLinkStart = -1;
        var failedDoubleTitle = int.MaxValue;
        var failedSingleTitle = int.MaxValue;
        var failedParenTitle = int.MaxValue;
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

            var character = input[position];
            if (character == '\n')
            {
                var spaces = position;
                while (spaces > 0 && input[spaces - 1] == ' ')
                {
                    spaces--;
                }

                var hard = position - spaces >= 2;
                this.AddNode(hard ? DocumentationMarkdownKind.HardBreak : DocumentationMarkdownKind.SoftBreak, parent, this.MapStart(hard ? spaces : position), this.MapEnd(position + 1));
                position++;
            }
            else if (character == '\0')
            {
                this.AddDecoded(parent, position, position + 1, 0xFFFD);
                position++;
            }
            else if (character == '\\' && position + 1 < input.Length && (MarkdownUnicode.IsAsciiPunctuation(input[position + 1]) || input[position + 1] == '\n'))
            {
                if (input[position + 1] == '\n')
                {
                    this.AddNode(DocumentationMarkdownKind.HardBreak, parent, this.MapStart(position), this.MapEnd(position + 2));
                }
                else
                {
                    this.AddDecoded(parent, position, position + 2, input[position + 1]);
                }

                position += 2;
            }
            else if (character == '&' && TryEntity(input[position..], out var scalar, out var consumed))
            {
                this.AddDecoded(parent, position, position + consumed, scalar);
                position += consumed;
            }
            else if (character == '`')
            {
                if (!indexedCode)
                {
                    this.IndexCodeRuns(input);
                    indexedCode = true;
                }

                var runEnd = position + 1;
                while (runEnd < input.Length && input[runEnd] == '`')
                {
                    runEnd++;
                }

                var runLength = runEnd - position;
                var closing = this.FindCodeCloser(runLength, runEnd);
                if (closing >= 0)
                {
                    var node = this.AddNode(DocumentationMarkdownKind.Code, parent, this.MapStart(position), this.MapEnd(closing + runLength));
                    var content = input[runEnd..closing];
                    var begin = runEnd;
                    var end = closing;
                    if (content.Length >= 2 && content[0] is ' ' or '\n' && content[^1] is ' ' or '\n' && content.IndexOfAnyExcept(' ', '\n') >= 0)
                    {
                        begin++;
                        end--;
                    }

                    this.SetInlineText(node, input, begin, end, this.MapStart(begin), this.MapEnd(end), normalizeCode: true);
                    position = closing + runLength;
                }
                else
                {
                    this.AddPlainText(parent, input, position, runEnd);
                    position = runEnd;
                }
            }
            else if (character is '*' or '_')
            {
                var runEnd = position + 1;
                while (runEnd < input.Length && input[runEnd] == character)
                {
                    runEnd++;
                }

                var before = MarkdownUnicode.Before(input, position);
                var after = MarkdownUnicode.After(input, runEnd);
                before = before == 0 ? 0xFFFD : before;
                after = after == 0 ? 0xFFFD : after;
                var beforeSpace = MarkdownUnicode.IsWhitespace(before);
                var afterSpace = MarkdownUnicode.IsWhitespace(after);
                var beforePunctuation = MarkdownUnicode.IsPunctuation(before);
                var afterPunctuation = MarkdownUnicode.IsPunctuation(after);
                var left = !afterSpace && (!afterPunctuation || beforeSpace || beforePunctuation);
                var right = !beforeSpace && (!beforePunctuation || afterSpace || afterPunctuation);
                var node = this.AddInlineText(parent, input, position, runEnd);
                var delimiter = this.delimiters.Add(new() { Node = node, Previous = this.lastDelimiter, Character = (byte)character, Count = runEnd - position, OriginalRemainder = (byte)((runEnd - position) % 3), CanOpen = left && (character == '*' || !right || beforePunctuation), CanClose = right && (character == '*' || !left || afterPunctuation), });
                this.delimiters[this.lastDelimiter].Next = delimiter;
                this.lastDelimiter = delimiter;
                position = runEnd;
            }
            else if (character == '[')
            {
                var node = this.AddInlineText(parent, input, position, position + 1);
                this.lastBracket = this.brackets.Add(new(node, position, this.lastDelimiter, this.lastBracket));
                position++;
            }
            else if (character == ']' && this.lastBracket != 0)
            {
                var bracket = this.brackets[this.lastBracket];
                this.lastBracket = bracket.Previous;
                if (bracket.Position >= lastLinkStart && position + 1 < input.Length && input[position + 1] == '(')
                {
                    if (this.TryLink(input, position + 2, ref failedDoubleTitle, ref failedSingleTitle, ref failedParenTitle, out var destination, out var title, out var end))
                    {
                        this.ProcessEmphasis(bracket.Delimiter);
                        var node = bracket.Node;
                        var firstChild = this.nodes[node].Next;
                        var lastChild = this.links[parent].Last;
                        this.nodes[node].Kind = DocumentationMarkdownKind.Link;
                        this.nodes[node].End = this.MapEnd(end);
                        this.nodes[node].TextLength = 0;
                        this.nodes[node].Argument = this.values.Add(destination);
                        if (title is { } titleValue)
                        {
                            this.values.Add(titleValue);
                            this.nodes[node].Flags |= MarkdownNodeFlags.HasTitle;
                        }

                        this.nodes[node].First = firstChild;
                        this.links[node].Last = firstChild == 0 ? 0 : lastChild;
                        this.nodes[node].Next = 0;
                        this.links[parent].Last = node;
                        var height = 0;
                        if (firstChild != 0)
                        {
                            this.links[firstChild].Previous = 0;
                            for (var child = firstChild; child != 0; child = this.nodes[child].Next)
                            {
                                this.nodes[child].Parent = node;
                                height = Math.Max(height, this.links[child].Level + 1);
                            }
                        }

                        this.SetHeight(node, height);
                        this.lastPlainText = 0;
                        lastLinkStart = bracket.Position;
                        position = end;
                        continue;
                    }
                }

                this.AddPlainText(parent, input, position, position + 1);
                position++;
            }
            else if (character == '<' && TryAutoLink(input, position, out var autoEnd, out var email))
            {
                var node = this.AddNode(DocumentationMarkdownKind.AutoLink, parent, this.MapStart(position), this.MapEnd(autoEnd));
                var target = input[(position + 1)..(autoEnd - 1)];
                if (this.inlineHasNul && target.Contains('\0'))
                {
                    // Email syntax cannot contain NUL. URI autolinks share one
                    // replaced value for destination and label; do not decode
                    // their backslashes or character references.
                    var value = this.values.Add(new(this.CreateText(target, normalizeCode: false)));
                    this.nodes[node].Argument = value;
                    this.nodes[node].TextStart = ~value;
                    this.nodes[node].TextLength = target.Length;
                }
                else
                {
                    this.nodes[node].Argument = this.values.Add(email ? new(string.Concat("mailto:", target)) : this.SliceValue(input, position + 1, autoEnd - 1));
                    this.SetInlineText(node, input, position + 1, autoEnd - 1, this.MapStart(position + 1), this.MapEnd(autoEnd - 1), normalizeCode: false);
                }
                lastLinkStart = position;
                position = autoEnd;
            }
            else
            {
                this.AddPlainText(parent, input, position, position + 1);
                position++;
            }
        }

        this.ProcessEmphasis(0);
        this.maxDepth = Math.Max(this.maxDepth, this.inlineDepth + 1);
    }

    private void ProcessEmphasis(int bottom)
    {
        Span<int> openersBottom = stackalloc int[12];
        openersBottom.Fill(bottom);
        var closer = this.delimiters[bottom].Next;
        while (closer != 0)
        {
            this.cancellationToken.ThrowIfCancellationRequested();
            var close = this.delimiters[closer];
            if (!close.CanClose)
            {
                closer = close.Next;
                continue;
            }

            // The rule of three uses original physical run lengths even after
            // some markers have been consumed by an inner emphasis node.
            var bucket = (close.Character == '*' ? 0 : 6) + (close.CanOpen ? 3 : 0) + close.OriginalRemainder;
            var opener = close.Previous;
            while (opener > openersBottom[bucket])
            {
                var open = this.delimiters[opener];
                if (open.CanOpen && open.Character == close.Character && (!(open.CanClose || close.CanOpen) || (open.OriginalRemainder + close.OriginalRemainder) % 3 != 0 || (open.OriginalRemainder == 0 && close.OriginalRemainder == 0)))
                {
                    break;
                }

                opener = open.Previous;
            }

            if (opener <= openersBottom[bucket])
            {
                openersBottom[bucket] = close.Previous;
                var next = close.Next;
                if (!close.CanOpen)
                {
                    this.RemoveDelimiter(closer);
                }

                closer = next;
                continue;
            }

            var opening = this.delimiters[opener];
            var use = opening.Count >= 2 && close.Count >= 2 ? 2 : 1;
            var openNode = opening.Node;
            var closeNode = close.Node;
            var first = this.nodes[openNode].Next;
            var last = this.links[closeNode].Previous;
            var parent = this.nodes[openNode].Parent;
            // Allocate without linking at the tail: the wrapper replaces the enclosed sibling range.
            var wrapper = this.nodes.Add(new() { Kind = use == 2 ? DocumentationMarkdownKind.Strong : DocumentationMarkdownKind.Emphasis, Parent = parent, Next = closeNode, First = first, Start = this.nodes[openNode].End - use, End = this.nodes[closeNode].Start + use, });
            this.links.Add(new() { Previous = openNode, Last = last });
            this.nodes[openNode].Next = wrapper;
            this.links[closeNode].Previous = wrapper;
            this.links[first].Previous = 0;
            this.nodes[last].Next = 0;
            var height = 1;
            for (var child = first; child != 0; child = this.nodes[child].Next)
            {
                this.nodes[child].Parent = wrapper;
                height = Math.Max(height, this.links[child].Level + 1);
            }

            this.SetHeight(wrapper, height);
            for (var delimiter = opening.Next; delimiter != closer;)
            {
                var next = this.delimiters[delimiter].Next;
                this.RemoveDelimiter(delimiter);
                delimiter = next;
            }

            this.nodes[openNode].End -= use;
            this.nodes[openNode].TextLength -= use;
            this.nodes[closeNode].Start += use;
            this.nodes[closeNode].TextStart += use;
            this.nodes[closeNode].TextLength -= use;
            this.delimiters[opener].Count -= use;
            this.delimiters[closer].Count -= use;
            if (this.delimiters[opener].Count == 0)
            {
                this.RemoveNode(openNode);
                this.RemoveDelimiter(opener);
            }

            if (this.delimiters[closer].Count == 0)
            {
                var next = this.delimiters[closer].Next;
                this.RemoveNode(closeNode);
                this.RemoveDelimiter(closer);
                closer = next;
            }
        }

        var remove = this.delimiters[bottom].Next;
        while (remove != 0)
        {
            var next = this.delimiters[remove].Next;
            this.RemoveDelimiter(remove);
            remove = next;
        }
    }

    // An inline wrapper's leaves are at (block depth + height + 1). Fail early.
    private void SetHeight(int node, int height)
    {
        this.links[node].Level = height;
        var depth = this.inlineDepth + height + 1;
        if (depth > this.maximumDepth)
        {
            throw new DocumentationMarkdownLimitException(this.maximumDepth);
        }

        this.maxDepth = Math.Max(this.maxDepth, depth);
    }

    private void RemoveDelimiter(int id)
    {
        var delimiter = this.delimiters[id];
        this.delimiters[delimiter.Previous].Next = delimiter.Next;
        if (delimiter.Next != 0)
        {
            this.delimiters[delimiter.Next].Previous = delimiter.Previous;
        }
        else
        {
            this.lastDelimiter = delimiter.Previous;
        }
    }

    private int AddInlineText(int parent, ReadOnlySpan<char> input, int start, int end)
    {
        var sourceStart = this.MapStart(start);
        var sourceEnd = this.MapEnd(end);
        var id = this.AddNode(DocumentationMarkdownKind.Text, parent, sourceStart, sourceEnd);
        this.SetInlineText(id, input, start, end, sourceStart, sourceEnd, normalizeCode: false);
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
        this.SetInlineText(id, input, start, end, sourceStart, sourceEnd, normalizeCode: false);
        this.lastPlainText = this.nodes[id].TextStart >= 0 ? id : 0;
    }

    private void AddDecoded(int parent, int start, int end, int scalar)
    {
        var id = this.AddNode(DocumentationMarkdownKind.Text, parent, this.MapStart(start), this.MapEnd(end));
        var first = scalar <= 0xFFFF ? scalar : 0xD800 + ((scalar - 0x10000) >> 10);
        var second = scalar <= 0xFFFF ? 0 : 0xDC00 + ((scalar - 0x10000) & 1023);
        this.nodes[id].TextStart = int.MinValue;
        this.nodes[id].TextLength = second == 0 ? 1 : 2;
        this.nodes[id].Argument = BitConverter.IsLittleEndian ? first | (second << 16) : (first << 16) | second;
    }

    private void SetInlineText(int id, ReadOnlySpan<char> input, int start, int end, int sourceStart, int sourceEnd, bool normalizeCode)
    {
        // NUL-containing autolinks are handled above; other non-code runs stop
        // at NULs and line endings. Only code needs normalization here.
        var special = !normalizeCode ? -1 : this.inlineHasNul ? input[start..end].IndexOfAny('\n', '\0') : this.inlineMultiline ? input[start..end].IndexOf('\n') : -1;
        if (special < 0 && sourceEnd - sourceStart == end - start)
        {
            this.nodes[id].TextStart = sourceStart;
            this.nodes[id].TextLength = end - start;
        }
        else
        {
            this.SetValue(id, this.CreateText(input[start..end], normalizeCode));
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

    private void IndexCodeRuns(ReadOnlySpan<char> input)
    {
        var position = 0;
        var longest = 0;
        while (position < input.Length)
        {
            var delta = input[position..].IndexOf('`');
            if (delta < 0)
            {
                break;
            }

            var start = position + delta;
            position = start + 1;
            while (position < input.Length && input[position] == '`')
            {
                position++;
            }

            longest = Math.Max(longest, position - start);
            this.codeRuns.Add(new(start, position - start, -1));
        }

        this.longestCodeRun = longest;
        ((Span<int>)this.shortCodeHeads).Fill(-1);
        if (longest >= ShortCodeHeads)
        {
            EnsureBuffer(ref this.codeHeads, longest + 1);
            this.codeHeads.AsSpan(ShortCodeHeads, longest + 1 - ShortCodeHeads).Fill(-1);
        }

        Span<int> shortHeads = this.shortCodeHeads;
        for (var i = this.codeRuns.Count - 1; i >= 0; i--)
        {
            ref var run = ref this.codeRuns[i];
            ref var head = ref (run.Length < ShortCodeHeads ? ref shortHeads[run.Length] : ref this.codeHeads![run.Length]);
            run.Next = head;
            head = i;
        }
    }

    private int FindCodeCloser(int length, int after)
    {
        if (length > this.longestCodeRun)
        {
            return -1;
        }

        Span<int> shortHeads = this.shortCodeHeads;
        ref var head = ref (length < ShortCodeHeads ? ref shortHeads[length] : ref this.codeHeads![length]);
        var index = head;
        while (index >= 0 && this.codeRuns[index].Start < after)
        {
            index = this.codeRuns[index].Next;
        }

        head = index;
        return index < 0 ? -1 : this.codeRuns[index].Start;
    }

    // The matching close of every '(' (-1 when unmatched), computed once per block
    // so repeated failing destinations stay linear. Jumps between the few
    // interesting characters instead of testing each one.
    private void IndexParentheses(ReadOnlySpan<char> input)
    {
        EnsureBuffer(ref this.parentheses, input.Length);
        this.parentheses.AsSpan(0, input.Length).Fill(-1);
        var stack = new MarkdownBuffer<int>(stackalloc int[32]);
        try
        {
            var i = 0;
            var iterations = 0;
            while (i < input.Length)
            {
                var delta = input[i..].IndexOfAny(ParenthesisCharacters);
                if (delta < 0)
                {
                    break;
                }

                if ((++iterations & 1023) == 0)
                {
                    this.cancellationToken.ThrowIfCancellationRequested();
                }

                i += delta;
                var character = input[i];
                if (character == '(')
                {
                    stack.Add(i++);
                }
                else if (character == ')')
                {
                    if (stack.Count > 0)
                    {
                        this.parentheses![stack[--stack.Count]] = i;
                    }

                    i++;
                }
                else
                {
                    i += i + 1 < input.Length && MarkdownUnicode.IsAsciiPunctuation(input[i + 1]) ? 2 : 1;
                }
            }
        }
        finally
        {
            stack.Dispose();
        }
    }

    // First destination-ending character at or after position. Destinations are
    // requested in increasing order, so one cached search usually answers.
    private int NextDestinationStop(ReadOnlySpan<char> input, int position)
    {
        if (position < this.stopSearchedFrom || position > this.stopFound)
        {
            var delta = input[position..].IndexOfAny(DestinationStops);
            this.stopSearchedFrom = position;
            this.stopFound = delta < 0 ? input.Length : position + delta;
        }

        return this.stopFound;
    }

    private bool TryLink(ReadOnlySpan<char> input, int start, ref int failedDouble, ref int failedSingle, ref int failedParen, out MarkdownValue destination, out MarkdownValue? title, out int end)
    {
        destination = default;
        title = null;
        end = start;
        var position = start;
        SkipLinkWhitespace(input, ref position);
        var destinationStart = position;
        var destinationEnd = position;
        // Parse the destination first, even when it starts with a quote after whitespace.
        // An explicitly empty destination before a title is written as <>.
        if (position < input.Length && input[position] == '<')
        {
            destinationStart = ++position;
            while (position < input.Length && input[position] is not ('<' or '>' or '\n'))
            {
                if (input[position] == '\\' && position + 1 < input.Length && MarkdownUnicode.IsAsciiPunctuation(input[position + 1]))
                {
                    position++;
                }

                position++;
            }

            if (position == input.Length || input[position] != '>')
            {
                return false;
            }

            destinationEnd = position++;
        }
        else
        {
            // A bare destination may not start with '<' but may contain one later.
            while (position < input.Length && (input[position] > ' ' || input[position] == '\0') && input[position] is not (')' or '\u007F'))
            {
                if (input[position] == '\\' && position + 1 < input.Length && MarkdownUnicode.IsAsciiPunctuation(input[position + 1]))
                {
                    position += 2;
                }
                else if (input[position] == '(')
                {
                    if (!this.indexedParentheses)
                    {
                        this.IndexParentheses(input);
                        this.indexedParentheses = true;
                    }

                    var close = this.parentheses![position];
                    if (close < 0 || close >= this.NextDestinationStop(input, position))
                    {
                        return false;
                    }

                    position = close + 1;
                }
                else
                {
                    position++;
                }
            }

            destinationEnd = position;
        }

        var beforeWhitespace = position;
        SkipLinkWhitespace(input, ref position);
        var titleStart = position;
        var titleEnd = position;
        var hasTitle = position > beforeWhitespace && position < input.Length && input[position] is '\'' or '"' or '(';
        if (hasTitle)
        {
            var delimiter = input[position] == '(' ? ')' : input[position];
            ref var failed = ref (delimiter == '"' ? ref failedDouble : ref (delimiter == '\'' ? ref failedSingle : ref failedParen));
            titleStart = ++position;
            if (position >= failed)
            {
                return false;
            }

            var newline = false;
            while (position < input.Length && input[position] != delimiter)
            {
                if (input[position] == '\\' && position + 1 < input.Length && MarkdownUnicode.IsAsciiPunctuation(input[position + 1]))
                {
                    position += 2;
                    newline = false;
                    continue;
                }

                if (delimiter == ')' && input[position] == '(')
                {
                    return false;
                }

                if (input[position] == '\n')
                {
                    if (newline)
                    {
                        return false;
                    }

                    newline = true;
                }
                else if (input[position] is not (' ' or '\t'))
                {
                    newline = false;
                }

                position++;
            }

            if (position == input.Length)
            {
                failed = Math.Min(failed, titleStart);
                return false;
            }

            titleEnd = position++;
            SkipLinkWhitespace(input, ref position);
        }

        if (position == input.Length || input[position] != ')')
        {
            return false;
        }

        destination = this.DecodeValue(input, destinationStart, destinationEnd);
        if (hasTitle)
        {
            title = this.DecodeValue(input, titleStart, titleEnd);
        }

        end = position + 1;
        return true;
    }

    private static void SkipLinkWhitespace(ReadOnlySpan<char> input, ref int position)
    {
        while (position < input.Length && input[position] is ' ' or '\t' or '\n')
        {
            position++;
        }
    }

    private static bool TryAutoLink(ReadOnlySpan<char> input, int start, out int end, out bool email)
    {
        end = start + 1;
        email = false;
        while (end < input.Length && (input[end] > ' ' || input[end] == '\0') && input[end] is not ('<' or '>'))
        {
            end++;
        }

        if (end == input.Length || input[end] != '>')
        {
            return false;
        }

        var value = input[(start + 1)..end];
        var colon = value.IndexOf(':');
        if (colon is >= 2 and <= 32 && IsAsciiLetter(value[0]))
        {
            var valid = true;
            for (var i = 1; i < colon; i++)
            {
                valid &= IsAsciiLetter(value[i]) || char.IsAsciiDigit(value[i]) || value[i] is '+' or '-' or '.';
            }

            if (valid)
            {
                end++;
                return true;
            }
        }

        var at = value.IndexOf('@');
        if (at <= 0 || at == value.Length - 1)
        {
            return false;
        }

        for (var i = 0; i < at; i++)
        {
            if (!IsAsciiLetter(value[i]) && !char.IsAsciiDigit(value[i]) && !".!#$%&'*+/=?^_`{|}~-".Contains(value[i]))
            {
                return false;
            }
        }

        var labelStart = at + 1;
        for (var i = labelStart; i <= value.Length; i++)
        {
            if (i == value.Length || value[i] == '.')
            {
                if (i == labelStart || i - labelStart > 63 || value[labelStart] == '-' || value[i - 1] == '-')
                {
                    return false;
                }

                labelStart = i + 1;
            }
            else if (!IsAsciiLetter(value[i]) && !char.IsAsciiDigit(value[i]) && value[i] != '-')
            {
                return false;
            }
        }

        email = true;
        end++;
        return true;
    }

    private static bool IsAsciiLetter(char value) => (uint)((value | 0x20) - 'a') <= 'z' - 'a';

    // A value without escapes or references is a source slice until a string is needed.
    private MarkdownValue DecodeValue(ReadOnlySpan<char> input, int start, int end)
    {
        return input[start..end].IndexOfAny(DecodeCharacters) < 0 ? this.SliceValue(input, start, end) : new(this.Decode(input[start..end]));
    }

    private MarkdownValue SliceValue(ReadOnlySpan<char> input, int start, int end)
    {
        if (end == start)
        {
            return new(string.Empty);
        }

        var sourceStart = this.MapStart(start);
        return this.MapEnd(end) - sourceStart == end - start ? new(sourceStart, end - start) : new(input[start..end].ToString());
    }

    // Decoded text is never longer than its source, so pooled scratch suffices.
    private string Decode(ReadOnlySpan<char> input)
    {
        var special = input.IndexOfAny(DecodeCharacters);
        if (special < 0)
        {
            return input.ToString();
        }

        EnsureBuffer(ref this.decodeScratch, input.Length);
        var output = this.decodeScratch.AsSpan();
        input[..special].CopyTo(output);
        var written = special;
        for (var i = special; i < input.Length; i++)
        {
            if (input[i] == '\\' && i + 1 < input.Length && MarkdownUnicode.IsAsciiPunctuation(input[i + 1]))
            {
                output[written++] = input[++i];
            }
            else if (input[i] == '&' && TryEntity(input[i..], out var scalar, out var consumed))
            {
                if (scalar <= char.MaxValue)
                {
                    output[written++] = (char)scalar;
                }
                else
                {
                    output[written++] = (char)(0xD800 + ((scalar - 0x10000) >> 10));
                    output[written++] = (char)(0xDC00 + ((scalar - 0x10000) & 1023));
                }

                i += consumed - 1;
            }
            else
            {
                output[written++] = input[i] == '\0' ? '\uFFFD' : input[i];
            }
        }

        return new string(output[..written]);
    }

    private static bool TryEntity(ReadOnlySpan<char> input, out int scalar, out int consumed)
    {
        scalar = consumed = 0;
        if (input.Length < 3)
        {
            return false;
        }

        if (input[1] != '#')
        {
            var semicolon = input[..Math.Min(input.Length, 6)].IndexOf(';');
            if (semicolon < 0)
            {
                return false;
            }

            scalar = input[1..semicolon] switch
            {
                "amp" => '&',
                "lt" => '<',
                "gt" => '>',
                "quot" => '"',
                "apos" => '\'',
                _ => 0,
            };
            consumed = semicolon + 1;
            return scalar != 0;
        }

        var position = 2;
        var hex = input[position] is 'x' or 'X';
        if (hex)
        {
            position++;
        }

        var first = position;
        var maximum = hex ? 6 : 7;
        while (position < input.Length && position - first < maximum)
        {
            var digit = input[position] is >= '0' and <= '9' ? input[position] - '0' : hex && (uint)((input[position] | 0x20) - 'a') < 6 ? (input[position] | 0x20) - 'a' + 10 : -1;
            if (digit < 0)
            {
                break;
            }

            scalar = (scalar * (hex ? 16 : 10)) + digit;
            position++;
        }

        if (position == first || position == input.Length || input[position] != ';')
        {
            return false;
        }

        consumed = position + 1;
        if (scalar == 0 || scalar > 0x10FFFF || scalar is >= 0xD800 and <= 0xDFFF)
        {
            scalar = 0xFFFD;
        }

        return true;
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

    internal struct Delimiter
    {
        internal int Node;

        internal int Previous;

        internal int Next;

        internal int Count;

        internal byte Character;

        internal byte OriginalRemainder;

        internal bool CanOpen;

        internal bool CanClose;
    }

    internal readonly record struct Bracket(int Node, int Position, int Delimiter, int Previous);

    internal struct CodeRun(int start, int length, int next)
    {
        internal int Start = start;

        internal int Length = length;

        internal int Next = next;
    }
}
