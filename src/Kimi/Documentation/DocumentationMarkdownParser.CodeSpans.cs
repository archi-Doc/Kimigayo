// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Documentation;

#pragma warning disable SA1201, SA1202, SA1204, SA1401, SA1513, SA1600 // Parser records beside their construct.

// Code spans (CommonMark 6.1): a backtick run closes at the next run of the same length. The block's runs are indexed on the
// first backtick, chained by length, so each search for a closer is amortized constant.
internal ref partial struct DocumentationMarkdownParser
{
    private const int ShortCodeHeads = 8;

    // An unmatched run is literal text. The content drops one space or line ending at each end when both ends have one and it is
    // not all spaces and line endings.
    private int ParseCodeSpan(int parent, ReadOnlySpan<char> input, int position)
    {
        if (!this.indexedCode)
        {
            this.IndexCodeRuns(input);
            this.indexedCode = true;
        }

        var runEnd = position + 1;
        while (runEnd < input.Length && input[runEnd] == '`')
        {
            runEnd++;
        }

        var runLength = runEnd - position;
        var closing = this.FindCodeCloser(runLength, runEnd);
        if (closing < 0)
        {
            this.AddPlainText(parent, input, position, runEnd);
            return runEnd;
        }

        var node = this.AddNode(DocumentationMarkdownKind.Code, parent, this.MapStart(position), this.MapEnd(closing + runLength));
        var content = input[runEnd..closing];
        var begin = runEnd;
        var end = closing;
        if (content.Length >= 2 && content[0] is ' ' or '\n' && content[^1] is ' ' or '\n' && content.IndexOfAnyExcept(' ', '\n') >= 0)
        {
            begin++;
            end--;
        }

        this.SetCodeText(node, input, begin, end);
        return closing + runLength;
    }

    // Code content turns line endings into spaces and NULs into U+FFFD. Content without a line ending lies on one line, so
    // SetInlineText borrows its slice.
    private void SetCodeText(int id, ReadOnlySpan<char> input, int start, int end)
    {
        var content = input[start..end];
        var normalized = this.inlineHasNul ? content.ContainsAny('\n', '\0') : this.inlineMultiline && content.Contains('\n');
        if (normalized)
        {
            this.SetValue(id, this.CreateText(content, normalizeCode: true));
        }
        else
        {
            this.SetInlineText(id, input, start, end, this.MapStart(start), this.MapEnd(end));
        }
    }

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

    [System.Runtime.CompilerServices.InlineArray(ShortCodeHeads)]
    private struct CodeHeadArray
    {
        private int element;
    }

    internal struct CodeRun(int start, int length, int next)
    {
        internal int Start = start;

        internal int Length = length;

        internal int Next = next;
    }
}
