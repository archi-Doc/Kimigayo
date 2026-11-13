// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;

namespace Kimi.Documentation;

#pragma warning disable SA1201, SA1202, SA1204, SA1401, SA1513, SA1600 // Private parser records and state-machine layout.

// A single-use parser living on the caller's stack: no parser object is allocated,
// and small documents finish entirely in caller-provided stack scratch.
// Call Parse once and then Dispose on the same variable. A defensive copy (for
// example a read-only `using var` local) would mutate the copy and never return
// its rented scratch to the pool.
internal ref partial struct DocumentationMarkdownParser
{
    private readonly string text;

    private readonly CancellationToken cancellationToken;

    private readonly int maximumDepth;

    private readonly bool hasNul;

    private MarkdownBuffer<MarkdownNodeData> nodes;

    // Parser-only sibling/tail links and depth/height, parallel to nodes.
    private MarkdownBuffer<NodeLinks> links;

    private MarkdownBuffer<MarkdownValue> values;

    private MarkdownBuffer<Container> containers;

    private MarkdownBuffer<ContentLine> lines;

    private int paragraph;

    private int fence;

    private int fenceLength;

    private int fenceIndent;

    private char fenceCharacter;

    private bool previousListBlank;

    private int detachedNodes;

    // The deepest node depth so far; exact, so no final tree walk is needed.
    private int maxDepth;

    private int spacesValue = -1;

    // Inline scratch; declared here because partial struct fields need one ordering.
    private MarkdownBuffer<Delimiter> delimiters;

    private MarkdownBuffer<Bracket> brackets;

    private MarkdownBuffer<CodeRun> codeRuns;

    // Chain heads of unmatched backtick runs, indexed by run length: short runs
    // inline, longer runs (rare) in a pooled array.
    private CodeHeadArray shortCodeHeads;

    private int[]? codeHeads;

    private int longestCodeRun;

    private bool indexedCode;

    private bool indexedParentheses;

    // A bracket opened before the last link's start makes no link: links do not nest.
    private int lastLinkStart;

    private TitleFailures failedTitles;

    private char[]? inlineCharacters;

    private int[]? parentheses;

    private int stopSearchedFrom;

    private int stopFound;

    private char[]? decodeScratch;

    private int inlineBase;

    // Joined multi-line input maps back through its content lines; 0 means the
    // input is one contiguous source slice starting at inlineBase.
    private int mappedFirst;

    private int mappedCount;

    private int inlineDepth;

    private int lastDelimiter;

    private int lastBracket;

    // The most recent plain text node; adjacent plain runs extend it in place.
    private int lastPlainText;

    private bool inlineHasNul;

    private bool inlineMultiline;

    internal DocumentationMarkdownParser(string text, CancellationToken cancellationToken, int maximumDepth)
        : this(text, cancellationToken, maximumDepth, text.Contains('\0'))
    {
    }

    internal DocumentationMarkdownParser(string text, CancellationToken cancellationToken, int maximumDepth, bool hasNul)
    {
        this.text = text;
        this.cancellationToken = cancellationToken;
        this.maximumDepth = maximumDepth;
        this.hasNul = hasNul;
    }

    internal DocumentationMarkdownParser(string text, CancellationToken cancellationToken, int maximumDepth, bool hasNul, in Scratch scratch)
        : this(text, cancellationToken, maximumDepth, hasNul)
    {
        this.nodes = new(scratch.Nodes);
        this.links = new(scratch.Links);
        this.values = new(scratch.Values);
        this.containers = new(scratch.Containers);
        this.lines = new(scratch.Lines);
        this.delimiters = new(scratch.Delimiters);
        this.brackets = new(scratch.Brackets);
        this.codeRuns = new(scratch.CodeRuns);
    }

    internal DocumentationMarkdownDocument Parse(DocumentationText? source)
    {
        var length = this.text.Length;
        this.nodes.EnsureCapacity((length >> 4) + 8);
        this.links.EnsureCapacity((length >> 4) + 8);
        this.nodes.Add(new() { Kind = DocumentationMarkdownKind.Document, End = length });
        this.links.Add(default);
        this.containers.Add(new(0, 0, '\0'));
        var start = 0;
        while (start < length)
        {
            this.cancellationToken.ThrowIfCancellationRequested();
            var delta = this.text.AsSpan(start).IndexOf('\n');
            var end = delta < 0 ? length : start + delta;
            this.ParseLine(start, end);
            start = end + 1;
        }

        var blockCount = this.nodes.Count;
        for (var id = 1; id < blockCount; id++)
        {
            if (this.nodes[id].Kind is DocumentationMarkdownKind.Paragraph or DocumentationMarkdownKind.Heading)
            {
                this.cancellationToken.ThrowIfCancellationRequested();
                this.ParseInlines(id);
            }
        }

        if (this.maxDepth > this.maximumDepth)
        {
            throw new DocumentationMarkdownLimitException(this.maximumDepth);
        }

        this.cancellationToken.ThrowIfCancellationRequested();
        return new(this.text, this.FreezeNodes(), this.values.Span.ToArray(), source);
    }

    public void Dispose()
    {
        this.nodes.Dispose();
        this.links.Dispose();
        this.values.Dispose();
        this.containers.Dispose();
        this.lines.Dispose();
        this.DisposeInlineBuffers();
    }

    // A line in two phases (CommonMark's appendix "A parsing strategy"): the open containers it continues, then the blocks it
    // starts or the open block it continues.
    private void ParseLine(int start, int end)
    {
        var nonblankEnd = end;
        while (nonblankEnd > start && this.text[nonblankEnd - 1] is ' ' or '\t')
        {
            nonblankEnd--;
        }

        var cursor = new Cursor(start, end, nonblankEnd);
        this.ContinueContainers(ref cursor);
        if (this.fence != 0)
        {
            this.ContinueFence(cursor);
        }
        else if (cursor.IsBlank())
        {
            this.AddBlankLine(cursor, start);
        }
        else
        {
            this.StartBlocks(cursor);
        }
    }

    // A quote continues past its '>' marker, a list item past its content indentation or on a blank line. The containers from
    // the first one the line does not continue close.
    private void ContinueContainers(ref Cursor cursor)
    {
        // Blank lines need no per-level list-prefix checks. Remember the first quote
        // in each frame so absent quote markers still close the right containers.
        var matched = cursor.IsBlank() && this.containers[this.containers.Count - 1].FirstQuote == 0 ? this.containers.Count : 1;
        for (; matched < this.containers.Count; matched++)
        {
            ref var container = ref this.containers[matched];
            var kind = this.nodes[container.Node].Kind;
            var candidate = cursor;
            if (kind == DocumentationMarkdownKind.Quote)
            {
                var indent = candidate.SkipWhitespace(this.text);
                if (indent > 3 || candidate.Position == candidate.End || this.text[candidate.Position] != '>')
                {
                    break;
                }

                candidate.Advance();
                candidate.ConsumeWhitespace(this.text, 1);
            }
            else if (kind == DocumentationMarkdownKind.ListItem)
            {
                if (!candidate.IsBlank() && candidate.ConsumeWhitespace(this.text, container.Indent) != container.Indent)
                {
                    break;
                }
            }

            cursor = candidate;
        }

        if (matched != this.containers.Count)
        {
            if (matched > 1 && this.nodes[this.containers[matched].Node].Kind == DocumentationMarkdownKind.ListItem)
            {
                this.containers[matched - 1].PreviousItemBlank = this.containers[matched].Blank || this.previousListBlank;
            }

            this.containers.Count = matched;
            this.paragraph = 0;
            this.fence = 0;
        }
    }

    // A blank line ends a paragraph and, in a list, records the blank for looseness. Explicit quote markers are syntax even on
    // an otherwise empty line.
    private void AddBlankLine(Cursor cursor, int start)
    {
        this.paragraph = 0;
        var top = this.containers.Count - 1;
        var kind = this.nodes[this.containers[top].Node].Kind;
        this.previousListBlank = kind
            is DocumentationMarkdownKind.ListItem or DocumentationMarkdownKind.BulletList or DocumentationMarkdownKind.OrderedList;
        if (kind == DocumentationMarkdownKind.ListItem)
        {
            if (this.nodes[this.containers[top].Node].First == 0)
            {
                // An empty item cannot adopt a block after a separating blank.
                this.containers[top - 1].PreviousItemBlank = true;
                this.containers.Count = top;
            }
            else
            {
                this.containers[top].Blank = true;
            }
        }

        if (cursor.Position > start)
        {
            this.UpdateEnds(cursor.End);
        }
    }

    // Blocks start until a leaf block takes the rest of the line: a quote or a list item opens a container whose content may
    // start another block. A line that starts no block interrupting the open paragraph continues it.
    private void StartBlocks(Cursor cursor)
    {
        var end = cursor.End;
        var followsListBlank = this.previousListBlank;
        this.previousListBlank = false;
        while (true)
        {
            var content = cursor;
            var indent = content.SkipWhitespace(this.text);
            var position = content.Position;
            var marker = indent <= 3 && position < end && this.text[position] is '-' or '+' or '*' or (>= '0' and <= '9')
                ? this.ReadListMarker(content)
                : default;
            var top = this.containers.Count - 1;
            var parent = this.containers[top].Node;
            var parentKind = this.nodes[parent].Kind;
            var continuingList = parentKind is DocumentationMarkdownKind.BulletList or DocumentationMarkdownKind.OrderedList;
            if (continuingList && (marker.Width == 0 || marker.Delimiter != this.containers[top].Marker))
            {
                this.containers.Count--;
                continue;
            }

            var afterMarker = content;
            var character = position < end ? this.text[position] : '\0';
            var headingLevel = indent <= 3 && character == '#' ? this.ReadHeadingLevel(ref afterMarker) : 0;
            var fenceLength = indent <= 3 && character is '`' or '~' ? this.ReadFenceLength(ref afterMarker, character) : 0;
            var startsQuote = indent <= 3 && character == '>';
            if (this.paragraph != 0 && !(startsQuote || headingLevel != 0 || fenceLength != 0 || marker.InterruptsParagraph))
            {
                this.AddContentLine(this.paragraph, content.Position, end);
                break;
            }

            this.paragraph = 0;
            if (this.containers[top].Blank || (followsListBlank && parentKind == DocumentationMarkdownKind.ListItem && this.nodes[parent].First != 0))
            {
                this.containers[top].Blank = false;
                if (parentKind == DocumentationMarkdownKind.ListItem)
                {
                    this.nodes[this.nodes[parent].Parent].Flags |= MarkdownNodeFlags.Loose;
                }
            }

            if (startsQuote)
            {
                cursor = this.StartQuote(parent, content);
                if (!cursor.IsBlank())
                {
                    continue;
                }
            }
            else if (marker.Width > 0)
            {
                cursor = this.StartListItem(parent, marker, position, indent);
                if (!cursor.IsBlank())
                {
                    continue;
                }
            }
            else if (headingLevel != 0)
            {
                this.AddHeading(parent, afterMarker, position, headingLevel);
            }
            else if (fenceLength != 0)
            {
                this.OpenFence(parent, afterMarker, position, indent, fenceLength);
            }
            else
            {
                this.paragraph = this.AddBlock(DocumentationMarkdownKind.Paragraph, parent, position, end);
                this.AddContentLine(this.paragraph, position, end);
            }

            break;
        }

        this.UpdateEnds(end);
    }

    // A block quote's content follows its '>' and one optional space.
    private Cursor StartQuote(int parent, Cursor content)
    {
        var quote = this.AddNode(DocumentationMarkdownKind.Quote, parent, content.Position, content.End);
        this.Push(new(quote, 0, '\0'));
        content.Advance();
        content.ConsumeWhitespace(this.text, 1);
        return content;
    }

    // A list item opens a list unless it continues the open one; its content starts after the marker's width and padding.
    private Cursor StartListItem(int parent, in ListMarker marker, int position, int indent)
    {
        var end = marker.Content.End;
        if (this.nodes[parent].Kind is not (DocumentationMarkdownKind.BulletList or DocumentationMarkdownKind.OrderedList))
        {
            parent = this.AddNode(marker.Ordered ? DocumentationMarkdownKind.OrderedList : DocumentationMarkdownKind.BulletList, parent, position, end);
            this.nodes[parent].Argument = marker.Number;
            this.Push(new(parent, 0, marker.Delimiter));
        }
        else if (this.containers[this.containers.Count - 1].PreviousItemBlank)
        {
            this.nodes[parent].Flags |= MarkdownNodeFlags.Loose;
        }

        var item = this.AddNode(DocumentationMarkdownKind.ListItem, parent, position, end);
        this.Push(new(item, indent + marker.Width + marker.Padding, '\0'));
        return marker.Content;
    }

    // The level of an ATX heading's opening sequence: 1 to 6 '#' before a space, a tab or the line end; else 0.
    private int ReadHeadingLevel(ref Cursor cursor)
    {
        var level = cursor.Count(this.text, '#');
        return level <= 6 && (cursor.Position >= cursor.End || this.text[cursor.Position] is ' ' or '\t') ? level : 0;
    }

    // An ATX heading's content lies between its opening sequence and an optional closing sequence of '#', without the spaces
    // and tabs around it.
    private void AddHeading(int parent, Cursor afterMarker, int position, int level)
    {
        var end = afterMarker.End;
        afterMarker.SkipWhitespace(this.text);
        var contentEnd = end;
        while (contentEnd > afterMarker.Position && this.text[contentEnd - 1] is ' ' or '\t')
        {
            contentEnd--;
        }

        var closing = contentEnd;
        while (closing > afterMarker.Position && this.text[closing - 1] == '#')
        {
            closing--;
        }

        if (closing < contentEnd && (closing == afterMarker.Position || this.text[closing - 1] is ' ' or '\t'))
        {
            contentEnd = closing;
            while (contentEnd > afterMarker.Position && this.text[contentEnd - 1] is ' ' or '\t')
            {
                contentEnd--;
            }
        }

        var heading = this.AddBlock(DocumentationMarkdownKind.Heading, parent, position, end);
        this.nodes[heading].Argument = level;
        this.AddContentLine(heading, afterMarker.Position, contentEnd);
    }

    private ListMarker ReadListMarker(Cursor cursor)
    {
        var start = cursor.Position;
        if (start == cursor.End)
        {
            return default;
        }

        var character = this.text[start];
        var ordered = character is >= '0' and <= '9';
        var number = 0;
        if (ordered)
        {
            var digits = 0;
            while (cursor.Position < cursor.End && this.text[cursor.Position] is >= '0' and <= '9')
            {
                if (++digits > 9)
                {
                    return default;
                }

                number = (number * 10) + this.text[cursor.Position] - '0';
                cursor.Advance();
            }

            if (cursor.Position == cursor.End || this.text[cursor.Position] is not ('.' or ')'))
            {
                return default;
            }

            character = this.text[cursor.Position];
        }
        else if (character is not ('-' or '+' or '*'))
        {
            return default;
        }

        cursor.Advance();
        var width = cursor.Position - start;
        if (cursor.Position < cursor.End && this.text[cursor.Position] is not (' ' or '\t'))
        {
            return default;
        }

        var body = cursor;
        var spaces = body.SkipWhitespace(this.text);
        var empty = body.Position == body.End;
        if (empty || spaces > 4)
        {
            cursor.ConsumeWhitespace(this.text, 1);
            return new(width, 1, character, ordered, number, cursor, empty);
        }

        return new(width, spaces, character, ordered, number, body, empty);
    }

    private void Push(Container container)
    {
        if (this.containers.Count > this.maximumDepth)
        {
            throw new DocumentationMarkdownLimitException(this.maximumDepth);
        }

        container.FirstQuote = this.containers[this.containers.Count - 1].FirstQuote;
        if (container.FirstQuote == 0 && this.nodes[container.Node].Kind == DocumentationMarkdownKind.Quote)
        {
            container.FirstQuote = this.containers.Count;
        }

        // A container at stack index i has depth i.
        this.maxDepth = Math.Max(this.maxDepth, this.containers.Add(container));
    }

    private void UpdateEnds(int end)
    {
        for (var i = 1; i < this.containers.Count; i++)
        {
            this.nodes[this.containers[i].Node].End = end;
        }
    }

    // Leaf blocks record their depth so inline nesting can be bounded exactly.
    private int AddBlock(DocumentationMarkdownKind kind, int parent, int start, int end)
    {
        var id = this.AddNode(kind, parent, start, end);
        var depth = this.containers.Count;
        this.links[id].Level = depth;
        this.maxDepth = Math.Max(this.maxDepth, depth);
        return id;
    }

    private void AddContentLine(int node, int start, int end)
    {
        ref var data = ref this.nodes[node];
        if (data.TextLength == 0)
        {
            data.TextStart = this.lines.Count;
        }

        data.TextLength++;
        data.End = Math.Max(data.End, end);
        this.lines.Add(new(start, end));
    }

    private int AddNode(DocumentationMarkdownKind kind, int parent, int start, int end)
    {
        var previous = this.links[parent].Last;
        var id = this.nodes.Add(new() { Kind = kind, Parent = parent, Start = start, End = end });
        this.links.Add(new() { Previous = previous });
        if (previous == 0)
        {
            this.nodes[parent].First = id;
        }
        else
        {
            this.nodes[previous].Next = id;
        }

        this.links[parent].Last = id;
        this.lastPlainText = 0;
        return id;
    }

    private void SetValue(int node, string value)
    {
        this.nodes[node].TextStart = ~this.values.Add(new(value));
        this.nodes[node].TextLength = value.Length;
    }

    private void RemoveNode(int node)
    {
        var data = this.nodes[node];
        var previous = this.links[node].Previous;
        this.nodes[node].Flags |= MarkdownNodeFlags.Detached;
        this.detachedNodes++;
        if (previous == 0)
        {
            this.nodes[data.Parent].First = data.Next;
        }
        else
        {
            this.nodes[previous].Next = data.Next;
        }

        if (data.Next == 0)
        {
            this.links[data.Parent].Last = previous;
        }
        else
        {
            this.links[data.Next].Previous = previous;
        }
    }

    // Delimiter nodes are useful mutable scratch, but must not remain in the
    // retained snapshot. Compact once before any public identities exist.
    private MarkdownNodeData[] FreezeNodes()
    {
        if (this.detachedNodes == 0)
        {
            return this.nodes.Span.ToArray();
        }

        // The sibling links are dead by now; their Previous slots hold the compaction map.
        var total = this.nodes.Count;
        var result = new MarkdownNodeData[total - this.detachedNodes];
        var count = 0;
        for (var i = 0; i < total; i++)
        {
            if ((this.nodes[i].Flags & MarkdownNodeFlags.Detached) == 0)
            {
                this.links[i].Previous = count++;
            }
        }

        for (var i = 0; i < total; i++)
        {
            if ((i & 1023) == 0)
            {
                this.cancellationToken.ThrowIfCancellationRequested();
            }

            var node = this.nodes[i];
            if ((node.Flags & MarkdownNodeFlags.Detached) == 0)
            {
                node.Parent = this.links[node.Parent].Previous;
                node.First = this.links[node.First].Previous;
                node.Next = this.links[node.Next].Previous;
                result[this.links[i].Previous] = node;
            }
        }

        return result;
    }

    // Initial storage supplied by the caller's frame; growth rents from the pool.
    internal readonly ref struct Scratch(
        Span<MarkdownNodeData> nodes,
        Span<NodeLinks> links,
        Span<MarkdownValue> values,
        Span<Container> containers,
        Span<ContentLine> lines,
        Span<Delimiter> delimiters,
        Span<Bracket> brackets,
        Span<CodeRun> codeRuns)
    {
        internal readonly Span<MarkdownNodeData> Nodes = nodes;

        internal readonly Span<NodeLinks> Links = links;

        internal readonly Span<MarkdownValue> Values = values;

        internal readonly Span<Container> Containers = containers;

        internal readonly Span<ContentLine> Lines = lines;

        internal readonly Span<Delimiter> Delimiters = delimiters;

        internal readonly Span<Bracket> Brackets = brackets;

        internal readonly Span<CodeRun> CodeRuns = codeRuns;
    }

    internal struct Container(int node, int indent, char marker)
    {
        internal int Node = node;

        internal int Indent = indent;

        internal char Marker = marker;

        internal bool Blank;

        internal bool PreviousItemBlank;

        internal int FirstQuote;
    }

    // Sibling/tail links make delimiter edits O(1); Level is a block's depth or an
    // inline wrapper's height for the exact depth limit. Never retained.
    internal struct NodeLinks
    {
        internal int Previous;

        internal int Last;

        internal int Level;
    }

    internal struct ContentLine(int start, int end)
    {
        internal int Start = start;

        internal int End = end;

        // Offset within the joined inline input of a multi-line block.
        internal int Joined;
    }

    private readonly record struct ListMarker(int Width, int Padding, char Delimiter, bool Ordered, int Number, Cursor Content, bool Empty)
    {
        // A list item interrupts a paragraph only with content and, if ordered, from 1.
        internal bool InterruptsParagraph => this.Width > 0 && !this.Empty && (!this.Ordered || this.Number == 1);
    }

    private struct Cursor(int position, int end, int nonblankEnd)
    {
        internal int Position = position;

        internal int End = end;

        internal int Column;

        internal int VirtualSpaces;

        private readonly int nonblankEnd = nonblankEnd;

        internal void Advance()
        {
            this.Position++;
            this.Column++;
        }

        internal int Count(string text, char character)
        {
            var start = this.Position;
            while (this.Position < this.End && text[this.Position] == character)
            {
                this.Advance();
            }

            return this.Position - start;
        }

        internal readonly bool IsBlank()
        {
            return this.Position >= this.nonblankEnd;
        }

        internal int SkipWhitespace(string text) => this.ConsumeWhitespace(text, int.MaxValue);

        internal int ConsumeWhitespace(string text, int maximum)
        {
            if (this.VirtualSpaces == 0 && (maximum <= 0 || this.Position >= this.End || text[this.Position] is not (' ' or '\t')))
            {
                return 0;
            }

            var initial = this.Column;
            var pending = Math.Min(this.VirtualSpaces, maximum);
            this.VirtualSpaces -= pending;
            this.Column += pending;
            maximum -= pending;
            while (maximum > 0 && this.Position < this.End && text[this.Position] is ' ' or '\t')
            {
                var width = text[this.Position++] == '\t' ? 4 - (this.Column & 3) : 1;
                var take = Math.Min(width, maximum);
                this.Column += take;
                this.VirtualSpaces = width - take;
                maximum -= take;
            }

            return this.Column - initial;
        }
    }
}
