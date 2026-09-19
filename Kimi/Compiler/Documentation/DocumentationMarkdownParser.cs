// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;

namespace Kimi.Compiler.Documentation;

#pragma warning disable SA1201, SA1202, SA1204, SA1401, SA1513, SA1600 // Private parser records and state-machine layout.

internal sealed partial class DocumentationMarkdownParser(string text, CancellationToken cancellationToken, int maximumDepth) : IDisposable
{
    private readonly string text = text;

    private readonly CancellationToken cancellationToken = cancellationToken;

    private readonly int maximumDepth = maximumDepth;

    private MarkdownBuffer<MarkdownNodeData> nodes;

    private MarkdownBuffer<string> values;

    private MarkdownBuffer<Container> containers;

    private MarkdownBuffer<ContentLine> lines;

    private int paragraph;

    private int fence;

    private int fenceLength;

    private int fenceIndent;

    private char fenceCharacter;

    private bool previousListBlank;

    private int detachedNodes;

    internal DocumentationMarkdownDocument Parse(DocumentationText? source)
    {
        this.nodes.Add(new() { Kind = DocumentationMarkdownKind.Document, End = this.text.Length });
        this.containers.Add(new(0, 0, '\0'));
        var start = 0;
        while (start < this.text.Length)
        {
            this.cancellationToken.ThrowIfCancellationRequested();
            var length = this.text.AsSpan(start).IndexOf('\n');
            var end = length < 0 ? this.text.Length : start + length;
            this.ParseLine(start, end);
            start = end + 1;
        }

        var blockCount = this.nodes.Count;
        for (var id = 1; id < blockCount; id++)
        {
            this.cancellationToken.ThrowIfCancellationRequested();
            if (this.nodes[id].Kind is DocumentationMarkdownKind.Paragraph or DocumentationMarkdownKind.Heading)
            {
                this.ParseInlines(id);
            }
        }

        this.CheckDepth();
        this.cancellationToken.ThrowIfCancellationRequested();
        return new(this.text, this.FreezeNodes(), this.values.Span.ToArray(), source);
    }

    public void Dispose()
    {
        this.nodes.Dispose();
        this.values.Dispose();
        this.containers.Dispose();
        this.lines.Dispose();
        this.DisposeInlineBuffers();
    }

    private void ParseLine(int start, int end)
    {
        var nonblankEnd = end;
        while (nonblankEnd > start && this.text[nonblankEnd - 1] is ' ' or '\t')
        {
            nonblankEnd--;
        }

        var cursor = new Cursor(start, end, nonblankEnd);
        // Blank lines need no per-level list-prefix checks. Remember the first quote
        // in each frame so absent quote markers still close the right containers.
        var matched = nonblankEnd == start && this.containers[this.containers.Count - 1].FirstQuote == 0 ? this.containers.Count : 1;
        for (; matched < this.containers.Count; matched++)
        {
            ref var container = ref this.containers[matched];
            var kind = this.nodes[container.Node].Kind;
            var candidate = cursor;
            if (kind == DocumentationMarkdownKind.Quote)
            {
                var indent = candidate.SkipWhitespace(this.text);
                if (indent > 3 || candidate.Position == end || this.text[candidate.Position] != '>')
                {
                    break;
                }

                candidate.Advance();
                candidate.ConsumeWhitespace(this.text, 1);
            }
            else if (kind == DocumentationMarkdownKind.ListItem)
            {
                if (!candidate.IsBlank(this.text) && candidate.ConsumeWhitespace(this.text, container.Indent) != container.Indent)
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

        if (this.fence != 0)
        {
            this.previousListBlank = false;
            var closing = cursor;
            var indent = closing.SkipWhitespace(this.text);
            var count = closing.Count(this.text, this.fenceCharacter);
            this.UpdateEnds(end);
            this.nodes[this.fence].End = end;
            if (indent <= 3 && count >= this.fenceLength && closing.IsBlank(this.text))
            {
                this.fence = 0;
            }
            else
            {
                cursor.ConsumeWhitespace(this.text, this.fenceIndent);
                this.AddCodeLine(cursor, this.fence);
            }

            return;
        }

        if (cursor.IsBlank(this.text))
        {
            this.paragraph = 0;
            var top = this.containers.Count - 1;
            this.previousListBlank = this.nodes[this.containers[top].Node].Kind is DocumentationMarkdownKind.ListItem or DocumentationMarkdownKind.BulletList or DocumentationMarkdownKind.OrderedList;
            if (this.nodes[this.containers[top].Node].Kind == DocumentationMarkdownKind.ListItem)
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

            // Explicit quote markers are syntax even on an otherwise empty line.
            if (cursor.Position > start)
            {
                this.UpdateEnds(end);
            }

            return;
        }

        var followsListBlank = this.previousListBlank;
        this.previousListBlank = false;
        while (true)
        {
            var content = cursor;
            var indent = content.SkipWhitespace(this.text);
            var position = content.Position;
            var marker = indent <= 3 ? this.ReadListMarker(content) : default;
            var top = this.containers.Count - 1;
            var parent = this.containers[top].Node;
            var parentKind = this.nodes[parent].Kind;
            var continuingList = parentKind is DocumentationMarkdownKind.BulletList or DocumentationMarkdownKind.OrderedList;
            if (continuingList && (marker.Width == 0 || marker.Delimiter != this.containers[top].Marker))
            {
                this.containers.Count--;
                continue;
            }

            var headingLevel = 0;
            var openingFenceLength = 0;
            var afterMarker = content;
            var character = position < end ? this.text[position] : '\0';
            if (indent <= 3 && character == '#')
            {
                headingLevel = afterMarker.Count(this.text, '#');
                if (headingLevel > 6 || (afterMarker.Position < end && this.text[afterMarker.Position] is not (' ' or '\t')))
                {
                    headingLevel = 0;
                }
            }
            else if (indent <= 3 && character is '`' or '~')
            {
                openingFenceLength = afterMarker.Count(this.text, character);
                if (openingFenceLength < 3 || (character == '`' && this.text.AsSpan(afterMarker.Position, end - afterMarker.Position).Contains('`')))
                {
                    openingFenceLength = 0;
                }
            }

            var startsQuote = indent <= 3 && character == '>';
            if (this.paragraph != 0 && !startsQuote && headingLevel == 0 && openingFenceLength == 0 && (marker.Width == 0 || marker.Empty || (marker.Ordered && marker.Number != 1)))
            {
                this.AddContentLine(this.paragraph, content.Position, end);
                this.UpdateEnds(end);
                return;
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
                var quote = this.AddNode(DocumentationMarkdownKind.Quote, parent, position, end);
                this.Push(new(quote, 0, '\0'));
                content.Advance();
                content.ConsumeWhitespace(this.text, 1);
                cursor = content;
                if (cursor.IsBlank(this.text))
                {
                    this.UpdateEnds(end);
                    return;
                }

                continue;
            }

            if (marker.Width > 0)
            {
                if (!continuingList)
                {
                    parent = this.AddNode(marker.Ordered ? DocumentationMarkdownKind.OrderedList : DocumentationMarkdownKind.BulletList, parent, position, end);
                    this.nodes[parent].Argument = marker.Number;
                    this.Push(new(parent, 0, marker.Delimiter));
                }
                else if (this.containers[top].PreviousItemBlank)
                {
                    this.nodes[parent].Flags |= MarkdownNodeFlags.Loose;
                }

                var item = this.AddNode(DocumentationMarkdownKind.ListItem, parent, position, end);
                this.Push(new(item, indent + marker.Width + marker.Padding, '\0'));
                cursor = marker.Content;
                if (cursor.IsBlank(this.text))
                {
                    this.UpdateEnds(end);
                    return;
                }

                continue;
            }

            if (headingLevel != 0)
            {
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

                var heading = this.AddNode(DocumentationMarkdownKind.Heading, parent, position, end);
                this.nodes[heading].Argument = headingLevel;
                this.AddContentLine(heading, afterMarker.Position, contentEnd);
            }
            else if (openingFenceLength != 0)
            {
                this.fence = this.AddNode(DocumentationMarkdownKind.CodeBlock, parent, position, end);
                this.fenceLength = openingFenceLength;
                this.fenceCharacter = character;
                this.fenceIndent = indent;
                var info = this.Decode(this.text.AsSpan(afterMarker.Position, end - afterMarker.Position).Trim(" \t"));
                var languageEnd = 0;
                while (languageEnd < info.Length && !MarkdownUnicode.IsWhitespace(info[languageEnd]))
                {
                    languageEnd++;
                }

                if (languageEnd > 0)
                {
                    this.SetValue(this.fence, info[..languageEnd]);
                }
            }
            else
            {
                this.paragraph = this.AddNode(DocumentationMarkdownKind.Paragraph, parent, position, end);
                this.AddContentLine(this.paragraph, position, end);
            }

            this.UpdateEnds(end);
            return;
        }
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
        var padding = empty || spaces > 4 ? 1 : spaces;
        cursor.ConsumeWhitespace(this.text, padding);
        return new(width, padding, character, ordered, number, cursor, empty);
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

        this.containers.Add(container);
    }

    private void UpdateEnds(int end)
    {
        for (var i = 1; i < this.containers.Count; i++)
        {
            this.nodes[this.containers[i].Node].End = end;
        }
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

    private void AddCodeLine(Cursor cursor, int parent)
    {
        if (cursor.VirtualSpaces > 0)
        {
            var padding = this.AddNode(DocumentationMarkdownKind.Text, parent, cursor.Position - 1, cursor.Position);
            this.SetValue(padding, new string(' ', cursor.VirtualSpaces));
        }

        if (cursor.Position < cursor.End)
        {
            var node = this.AddNode(DocumentationMarkdownKind.Text, parent, cursor.Position, cursor.End);
            this.nodes[node].TextStart = cursor.Position;
            this.nodes[node].TextLength = cursor.End - cursor.Position;
            if (this.text.AsSpan(cursor.Position, cursor.End - cursor.Position).Contains('\0'))
            {
                this.SetValue(node, this.text[cursor.Position..cursor.End].Replace('\0', '\uFFFD'));
            }
        }

        // Fenced code has a final LF even when the physical last line has none.
        var lineEnd = cursor.End < this.text.Length ? cursor.End + 1 : cursor.End;
        this.AddNode(DocumentationMarkdownKind.SoftBreak, parent, cursor.End, lineEnd);
        this.nodes[parent].End = lineEnd;
        this.UpdateEnds(lineEnd);
    }

    private int AddNode(DocumentationMarkdownKind kind, int parent, int start, int end)
    {
        var previous = this.nodes[parent].Last;
        var id = this.nodes.Add(new() { Kind = kind, Parent = parent, Previous = previous, Start = start, End = end });
        if (previous == 0)
        {
            this.nodes[parent].First = id;
        }
        else
        {
            this.nodes[previous].Next = id;
        }

        this.nodes[parent].Last = id;
        return id;
    }

    private void SetValue(int node, string value)
    {
        this.nodes[node].TextStart = ~this.values.Add(value);
        this.nodes[node].TextLength = value.Length;
    }

    private void RemoveNode(int node)
    {
        var data = this.nodes[node];
        this.nodes[node].Flags |= MarkdownNodeFlags.Detached;
        this.detachedNodes++;
        if (data.Previous == 0)
        {
            this.nodes[data.Parent].First = data.Next;
        }
        else
        {
            this.nodes[data.Previous].Next = data.Next;
        }

        if (data.Next == 0)
        {
            this.nodes[data.Parent].Last = data.Previous;
        }
        else
        {
            this.nodes[data.Next].Previous = data.Previous;
        }
    }

    private void CheckDepth()
    {
        var depth = 0;
        var node = 0;
        while (true)
        {
            this.cancellationToken.ThrowIfCancellationRequested();
            if (this.nodes[node].First != 0)
            {
                node = this.nodes[node].First;
                if (++depth > this.maximumDepth)
                {
                    throw new DocumentationMarkdownLimitException(this.maximumDepth);
                }

                continue;
            }

            while (node != 0 && this.nodes[node].Next == 0)
            {
                node = this.nodes[node].Parent;
                depth--;
            }

            if (node == 0)
            {
                return;
            }

            node = this.nodes[node].Next;
        }
    }

    private MarkdownNodeData[] FreezeNodes()
    {
        if (this.detachedNodes == 0)
        {
            return this.nodes.Span.ToArray();
        }

        // Delimiter nodes are useful mutable scratch, but must not remain in the
        // retained snapshot. Compact once before any public identities exist.
        var map = ArrayPool<int>.Shared.Rent(this.nodes.Count);
        try
        {
            var count = 0;
            for (var i = 0; i < this.nodes.Count; i++)
            {
                if ((this.nodes[i].Flags & MarkdownNodeFlags.Detached) == 0)
                {
                    map[i] = count++;
                }
            }

            var result = new MarkdownNodeData[count];
            for (var i = 0; i < this.nodes.Count; i++)
            {
                if ((i & 1023) == 0)
                {
                    this.cancellationToken.ThrowIfCancellationRequested();
                }

                var node = this.nodes[i];
                if ((node.Flags & MarkdownNodeFlags.Detached) != 0)
                {
                    continue;
                }

                node.Parent = map[node.Parent];
                node.First = map[node.First];
                node.Last = map[node.Last];
                node.Previous = map[node.Previous];
                node.Next = map[node.Next];
                result[map[i]] = node;
            }

            return result;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(map);
        }
    }

    private struct Container(int node, int indent, char marker)
    {
        internal int Node = node;

        internal int Indent = indent;

        internal char Marker = marker;

        internal bool Blank;

        internal bool PreviousItemBlank;

        internal int FirstQuote;
    }

    private readonly record struct ContentLine(int Start, int End);

    private readonly record struct ListMarker(int Width, int Padding, char Delimiter, bool Ordered, int Number, Cursor Content, bool Empty);

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

        internal bool IsBlank(string text)
        {
            return this.Position >= this.nonblankEnd;
        }

        internal int SkipWhitespace(string text) => this.ConsumeWhitespace(text, int.MaxValue);

        internal int ConsumeWhitespace(string text, int maximum)
        {
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
