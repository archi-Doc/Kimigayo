// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Documentation;

#pragma warning disable SA1201, SA1202, SA1204, SA1401, SA1513, SA1600 // Delimiter algorithm and compact scratch records.

// Emphasis (CommonMark 6.2): a run of '*' or '_' is a text node and a delimiter whose flanking decides whether it can open or
// close; ProcessEmphasis pairs closers with openers and wraps the nodes between them in Emphasis or Strong.
internal ref partial struct DocumentationMarkdownParser
{
    private int AddDelimiterRun(int parent, ReadOnlySpan<char> input, int position)
    {
        var character = input[position];
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
        var delimiter = this.delimiters.Add(new()
        {
            Node = node,
            Previous = this.lastDelimiter,
            Character = (byte)character,
            Count = runEnd - position,
            OriginalRemainder = (byte)((runEnd - position) % 3),
            CanOpen = left && (character == '*' || !right || beforePunctuation),
            CanClose = right && (character == '*' || !left || afterPunctuation),
        });
        this.delimiters[this.lastDelimiter].Next = delimiter;
        this.lastDelimiter = delimiter;
        return runEnd;
    }

    // Pairs the delimiters above bottom, then removes them: unpaired ones stay literal text.
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
            var opener = this.FindOpener(close, openersBottom[bucket]);
            if (opener > openersBottom[bucket])
            {
                closer = this.Emphasize(opener, closer);
                continue;
            }

            openersBottom[bucket] = close.Previous;
            var next = close.Next;
            if (!close.CanOpen)
            {
                this.RemoveDelimiter(closer);
            }

            closer = next;
        }

        this.RemoveDelimiters(this.delimiters[bottom].Next, 0);
    }

    // The nearest opener above bottom that pairs with the closer: the same character, and not both run lengths summing to a
    // multiple of three when either run can both open and close (unless both are multiples of three). Bottom when none.
    private int FindOpener(in Delimiter close, int bottom)
    {
        var opener = close.Previous;
        while (opener > bottom)
        {
            var open = this.delimiters[opener];
            if (open.CanOpen && open.Character == close.Character &&
                (!(open.CanClose || close.CanOpen) || (open.OriginalRemainder + close.OriginalRemainder) % 3 != 0 ||
                (open.OriginalRemainder == 0 && close.OriginalRemainder == 0)))
            {
                break;
            }

            opener = open.Previous;
        }

        return opener;
    }

    // Wraps the nodes between the opener's and the closer's text in Strong (two markers from each run) or Emphasis (one), drops the
    // delimiters between them and the runs used up. Returns the closer to examine next.
    private int Emphasize(int opener, int closer)
    {
        var opening = this.delimiters[opener];
        var use = opening.Count >= 2 && this.delimiters[closer].Count >= 2 ? 2 : 1;
        var openNode = opening.Node;
        var closeNode = this.delimiters[closer].Node;
        var first = this.nodes[openNode].Next;
        var last = this.links[closeNode].Previous;
        var parent = this.nodes[openNode].Parent;
        // Allocate without linking at the tail: the wrapper replaces the enclosed sibling range.
        var wrapper = this.nodes.Add(new()
        {
            Kind = use == 2 ? DocumentationMarkdownKind.Strong : DocumentationMarkdownKind.Emphasis,
            Parent = parent,
            Next = closeNode,
            First = first,
            Start = this.nodes[openNode].End - use,
            End = this.nodes[closeNode].Start + use,
        });
        this.links.Add(new() { Previous = openNode, Last = last });
        this.nodes[openNode].Next = wrapper;
        this.links[closeNode].Previous = wrapper;
        this.links[first].Previous = 0;
        this.nodes[last].Next = 0;
        this.Adopt(wrapper, first);
        this.RemoveDelimiters(opening.Next, closer);
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

        if (this.delimiters[closer].Count != 0)
        {
            return closer;
        }

        var next = this.delimiters[closer].Next;
        this.RemoveNode(closeNode);
        this.RemoveDelimiter(closer);
        return next;
    }

    // Removes the delimiters from first up to (not including) end; 0 ends the list.
    private void RemoveDelimiters(int first, int end)
    {
        for (var delimiter = first; delimiter != end;)
        {
            var next = this.delimiters[delimiter].Next;
            this.RemoveDelimiter(delimiter);
            delimiter = next;
        }
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
}
