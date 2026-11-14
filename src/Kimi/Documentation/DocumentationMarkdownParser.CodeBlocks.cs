// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Documentation;

#pragma warning disable SA1201, SA1202, SA1204, SA1401, SA1513, SA1600 // Parser records beside their construct.

// Fenced code blocks (CommonMark 4.5): the opening fence, the lines inside, and the closing fence.
internal ref partial struct DocumentationMarkdownParser
{
    // The open fence's character, length and indentation decide its closing line and how much of each line is code.
    private void OpenFence(int parent, Cursor afterMarker, int position, int indent, int length)
    {
        this.fence = this.AddBlock(DocumentationMarkdownKind.CodeBlock, parent, position, afterMarker.End);
        this.fenceLength = length;
        this.fenceCharacter = this.text[position];
        this.fenceIndent = indent;
        this.SetLanguage(this.fence, afterMarker.Position, afterMarker.End);
    }

    // The length of an opening fence: three or more, with no backtick after a backtick fence; else 0.
    private int ReadFenceLength(ref Cursor cursor, char character)
    {
        var length = cursor.Count(this.text, character);
        return length < 3 || (character == '`' && this.text.AsSpan(cursor.Position, cursor.End - cursor.Position).Contains('`')) ? 0 : length;
    }

    // Inside the fence a line closes it or is code, less up to the fence's indentation.
    private void ContinueFence(Cursor cursor)
    {
        var end = cursor.End;
        this.previousListBlank = false;
        this.UpdateEnds(end);
        this.nodes[this.fence].End = end;
        if (this.ClosesFence(cursor))
        {
            this.fence = 0;
            return;
        }

        if (this.fenceIndent > 0)
        {
            cursor.ConsumeWhitespace(this.text, this.fenceIndent);
        }

        this.AddCodeLine(cursor, this.fence);
    }

    // Only a line starting with whitespace or the fence character can close the fence.
    private bool ClosesFence(Cursor cursor)
    {
        if (cursor.VirtualSpaces > 0 ||
            (cursor.Position < cursor.End && (this.text[cursor.Position] == this.fenceCharacter || this.text[cursor.Position] is ' ' or '\t')))
        {
            var indent = cursor.SkipWhitespace(this.text);
            var count = cursor.Count(this.text, this.fenceCharacter);
            return indent <= 3 && count >= this.fenceLength && cursor.IsBlank();
        }

        return false;
    }

    // The first word of the decoded info string. Borrow the source unless an
    // escape or character reference makes the decoded word differ.
    private void SetLanguage(int node, int infoStart, int end)
    {
        while (infoStart < end && this.text[infoStart] is ' ' or '\t')
        {
            infoStart++;
        }

        var wordEnd = infoStart;
        while (wordEnd < end && !MarkdownUnicode.IsWhitespace(this.text[wordEnd]))
        {
            wordEnd++;
        }

        if (wordEnd == infoStart)
        {
            return;
        }

        var word = this.text.AsSpan(infoStart, wordEnd - infoStart);
        if (word.IndexOfAny(DecodeCharacters) < 0)
        {
            this.nodes[node].TextStart = infoStart;
            this.nodes[node].TextLength = word.Length;
            return;
        }

        var decoded = this.Decode(word);
        var languageEnd = 0;
        while (languageEnd < decoded.Length && !MarkdownUnicode.IsWhitespace(decoded[languageEnd]))
        {
            languageEnd++;
        }

        if (languageEnd == decoded.Length)
        {
            this.SetValue(node, decoded);
        }
        else if (languageEnd > 0)
        {
            this.SetValue(node, decoded[..languageEnd]);
        }
    }

    private void AddCodeLine(Cursor cursor, int parent)
    {
        this.maxDepth = Math.Max(this.maxDepth, this.links[parent].Level + 1);
        if (cursor.VirtualSpaces > 0)
        {
            var padding = this.AddNode(DocumentationMarkdownKind.Text, parent, cursor.Position - 1, cursor.Position);
            if (this.spacesValue < 0)
            {
                this.spacesValue = this.values.Add(new("    "));
            }

            this.nodes[padding].TextStart = ~this.spacesValue;
            this.nodes[padding].TextLength = cursor.VirtualSpaces;
        }

        // Adjacent physical code lines can borrow one contiguous source slice.
        // Consumed quote/list prefixes, partial tabs and decoded NULs split slices.
        var lineEnd = cursor.End < this.text.Length ? cursor.End + 1 : cursor.End;
        if (cursor.Position < lineEnd)
        {
            var content = this.text.AsSpan(cursor.Position, lineEnd - cursor.Position);
            var last = this.links[parent].Last;
            if (this.hasNul && content.Contains('\0'))
            {
                var node = this.AddNode(DocumentationMarkdownKind.Text, parent, cursor.Position, lineEnd);
                this.SetValue(node, this.CreateText(content, normalizeCode: false));
            }
            else if (last != 0 && this.nodes[last].Kind == DocumentationMarkdownKind.Text && this.nodes[last].TextStart >= 0 &&
                this.nodes[last].End == cursor.Position)
            {
                this.nodes[last].TextLength += content.Length;
                this.nodes[last].End = lineEnd;
            }
            else
            {
                var node = this.AddNode(DocumentationMarkdownKind.Text, parent, cursor.Position, lineEnd);
                this.nodes[node].TextStart = cursor.Position;
                this.nodes[node].TextLength = content.Length;
            }
        }

        // Fenced code has a final LF even when the physical last line has none.
        if (cursor.End == this.text.Length)
        {
            this.AddNode(DocumentationMarkdownKind.SoftBreak, parent, cursor.End, lineEnd);
        }

        this.nodes[parent].End = lineEnd;
        this.UpdateEnds(lineEnd);
    }
}
