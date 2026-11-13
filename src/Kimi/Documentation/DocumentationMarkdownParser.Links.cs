// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;

namespace Kimi.Documentation;

#pragma warning disable SA1201, SA1202, SA1204, SA1401, SA1513, SA1600 // Parser records beside their construct.

// Inline links (CommonMark 6.3): '[' opens link text, and a ']' followed by "(destination title)" closes it. The per-block scans
// a failing link repeats (parenthesis matching, the next destination stop, titles that reach the end) are indexed or remembered
// once per block, so the pass stays linear.
internal ref partial struct DocumentationMarkdownParser
{
    private static readonly SearchValues<char> ParenthesisCharacters = SearchValues.Create("()\\");

    // NUL is logically U+FFFD, not a destination-ending control character.
    private static readonly SearchValues<char> DestinationStops = SearchValues.Create(
        "\x01\x02\x03\x04\x05\x06\x07\x08\t\n\x0B\x0C\r\x0E\x0F\x10\x11\x12\x13\x14\x15\x16\x17\x18\x19\x1A\x1B\x1C\x1D\x1E\x1F \x7F");

    private int OpenBracket(int parent, ReadOnlySpan<char> input, int position)
    {
        var node = this.AddInlineText(parent, input, position, position + 1);
        this.lastBracket = this.brackets.Add(new(node, position, this.lastDelimiter, this.lastBracket));
        return position + 1;
    }

    // A ']' closes the innermost open bracket. It ends a link when a link tail follows and no link starts inside the bracket.
    private int CloseBracket(int parent, ReadOnlySpan<char> input, int position)
    {
        if (this.lastBracket == 0)
        {
            return position;
        }

        var bracket = this.brackets[this.lastBracket];
        this.lastBracket = bracket.Previous;
        if (bracket.Position >= this.lastLinkStart && position + 1 < input.Length && input[position + 1] == '(' &&
            this.TryLinkTail(input, position + 2, out var tail))
        {
            this.AddLink(parent, input, bracket, tail);
            return tail.End;
        }

        return position;
    }

    // The bracket's text node becomes the link and adopts the nodes after it; emphasis inside the link text is resolved first.
    private void AddLink(int parent, ReadOnlySpan<char> input, Bracket bracket, in LinkTail tail)
    {
        this.ProcessEmphasis(bracket.Delimiter);
        var node = bracket.Node;
        var firstChild = this.nodes[node].Next;
        var lastChild = this.links[parent].Last;
        this.nodes[node].Kind = DocumentationMarkdownKind.Link;
        this.nodes[node].End = this.MapEnd(tail.End);
        this.nodes[node].TextLength = 0;
        this.nodes[node].Argument = this.values.Add(this.DecodeValue(input, tail.DestinationStart, tail.DestinationEnd));
        if (tail.TitleStart >= 0)
        {
            this.values.Add(this.DecodeValue(input, tail.TitleStart, tail.TitleEnd));
            this.nodes[node].Flags |= MarkdownNodeFlags.HasTitle;
        }

        this.nodes[node].First = firstChild;
        this.links[node].Last = firstChild == 0 ? 0 : lastChild;
        this.nodes[node].Next = 0;
        this.links[parent].Last = node;
        if (firstChild != 0)
        {
            this.links[firstChild].Previous = 0;
        }

        this.Adopt(node, firstChild);
        this.lastPlainText = 0;
        this.lastLinkStart = bracket.Position;
    }

    // A link tail after "](": a destination, a title after whitespace, and ')', with optional whitespace between them.
    private bool TryLinkTail(ReadOnlySpan<char> input, int start, out LinkTail tail)
    {
        tail = default;
        var position = start;
        SkipLinkWhitespace(input, ref position);
        // Parse the destination first, even when it starts with a quote after whitespace.
        // An explicitly empty destination before a title is written as <>.
        int destinationStart;
        int destinationEnd;
        if (position < input.Length && input[position] == '<')
        {
            destinationStart = position + 1;
            destinationEnd = ScanBracketedDestination(input, destinationStart);
            position = destinationEnd + 1;
        }
        else
        {
            destinationStart = position;
            destinationEnd = this.ScanBareDestination(input, position);
            position = destinationEnd;
        }

        if (destinationEnd < 0)
        {
            return false;
        }

        var beforeWhitespace = position;
        SkipLinkWhitespace(input, ref position);
        var titleStart = -1;
        var titleEnd = -1;
        if (position > beforeWhitespace && position < input.Length && input[position] is '\'' or '"' or '(')
        {
            titleStart = position + 1;
            titleEnd = this.ScanTitle(input, titleStart, input[position] == '(' ? ')' : input[position]);
            if (titleEnd < 0)
            {
                return false;
            }

            position = titleEnd + 1;
            SkipLinkWhitespace(input, ref position);
        }

        if (position == input.Length || input[position] != ')')
        {
            return false;
        }

        tail = new(destinationStart, destinationEnd, titleStart, titleEnd, position + 1);
        return true;
    }

    // The index of the '>' ending a destination in angle brackets, which holds no '<' or line ending; else -1.
    private static int ScanBracketedDestination(ReadOnlySpan<char> input, int position)
    {
        while (position < input.Length && input[position] is not ('<' or '>' or '\n'))
        {
            if (IsBackslashEscape(input, position))
            {
                position++;
            }

            position++;
        }

        return position < input.Length && input[position] == '>' ? position : -1;
    }

    // The end of a bare destination, which holds no space or control character and may not start with '<' but may contain one
    // later; -1 when a '(' in it has no match before the first destination-ending character.
    private int ScanBareDestination(ReadOnlySpan<char> input, int position)
    {
        while (position < input.Length && (input[position] > ' ' || input[position] == '\0') && input[position] is not (')' or '\u007F'))
        {
            if (IsBackslashEscape(input, position))
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
                    return -1;
                }

                position = close + 1;
            }
            else
            {
                position++;
            }
        }

        return position;
    }

    // The index of a title's closing delimiter, or -1: a title holds no blank line, and a parenthesized title no unescaped '('.
    // A title that reaches the end of the input fails for every later start with the same delimiter.
    private int ScanTitle(ReadOnlySpan<char> input, int start, char delimiter)
    {
        var kind = delimiter switch
        {
            '"' => 0,
            '\'' => 1,
            _ => 2,
        };
        if (start >= this.failedTitles[kind])
        {
            return -1;
        }

        var position = start;
        var newline = false;
        while (position < input.Length && input[position] != delimiter)
        {
            if (IsBackslashEscape(input, position))
            {
                position += 2;
                newline = false;
                continue;
            }

            if (delimiter == ')' && input[position] == '(')
            {
                return -1;
            }

            if (input[position] == '\n')
            {
                if (newline)
                {
                    return -1;
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
            this.failedTitles[kind] = Math.Min(this.failedTitles[kind], start);
            return -1;
        }

        return position;
    }

    private static void SkipLinkWhitespace(ReadOnlySpan<char> input, ref int position)
    {
        while (position < input.Length && input[position] is ' ' or '\t' or '\n')
        {
            position++;
        }
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
                    i += IsBackslashEscape(input, i) ? 2 : 1;
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

    internal readonly record struct Bracket(int Node, int Position, int Delimiter, int Previous);

    // A parsed link tail: the destination and title ranges in the inline input (TitleStart -1 without a title) and the end after
    // its ')'.
    private readonly record struct LinkTail(int DestinationStart, int DestinationEnd, int TitleStart, int TitleEnd, int End);

    // The earliest start of a failed title per closing delimiter ('"', '\'', ')').
    [System.Runtime.CompilerServices.InlineArray(3)]
    private struct TitleFailures
    {
        private int element;
    }
}
