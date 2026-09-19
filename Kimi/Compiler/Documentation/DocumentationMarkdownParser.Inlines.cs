// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Text;

namespace Kimi.Compiler.Documentation;

#pragma warning disable SA1201, SA1202, SA1204, SA1401, SA1513, SA1600 // Delimiter algorithm and compact scratch records.

internal sealed partial class DocumentationMarkdownParser
{
    private static readonly SearchValues<char> InlineCharacters = SearchValues.Create("*_`[\\&\n\0<]");

    private MarkdownBuffer<Delimiter> delimiters;

    private MarkdownBuffer<Bracket> brackets;

    private MarkdownBuffer<CodeRun> codeRuns;

    private Dictionary<int, int>? codeHeads;

    private int singleCodeHead;

    private int doubleCodeHead;

    private bool indexedParentheses;

    private char[]? inlineCharacters;

    private int[]? inlinePositions;

    private int[]? parentheses;

    private int[]? nextDestinationStop;

    private int inlineBase;

    private bool mappedInline;

    private int lastDelimiter;

    private int lastBracket;

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

        var length = 0;
        for (var i = 0; i < count; i++)
        {
            length += this.lines[first + i].End - this.lines[first + i].Start + (i == 0 ? 0 : 1);
        }

        ReadOnlySpan<char> input;
        this.mappedInline = count > 1;
        this.inlineBase = this.lines[first].Start;
        if (count == 1)
        {
            input = this.text.AsSpan(this.inlineBase, length).TrimEnd(" \t");
        }
        else
        {
            EnsureBuffer(ref this.inlineCharacters, length);
            EnsureBuffer(ref this.inlinePositions, length);
            var written = 0;
            for (var line = 0; line < count; line++)
            {
                var slice = this.lines[first + line];
                if (line > 0)
                {
                    this.inlineCharacters![written] = '\n';
                    this.inlinePositions![written++] = this.lines[first + line - 1].End;
                }

                this.text.AsSpan(slice.Start, slice.End - slice.Start).CopyTo(this.inlineCharacters.AsSpan(written));
                for (var p = slice.Start; p < slice.End; p++)
                {
                    this.inlinePositions![written++] = p;
                }
            }

            input = this.inlineCharacters.AsSpan(0, written).TrimEnd(" \t");
        }

        this.delimiters.Clear();
        this.brackets.Clear();
        this.codeRuns.Clear();
        this.codeHeads?.Clear();
        this.singleCodeHead = this.doubleCodeHead = -1;
        this.indexedParentheses = false;
        if (input.IndexOfAny(InlineCharacters) < 0)
        {
            if (!input.IsEmpty)
            {
                this.AddInlineText(parent, input, 0, input.Length);
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
        while (position < input.Length)
        {
            this.cancellationToken.ThrowIfCancellationRequested();
            var plain = input[position..].IndexOfAny(InlineCharacters);
            if (plain < 0)
            {
                plain = input.Length - position;
            }

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
                    this.AddInlineText(parent, input, position, textEnd);
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

                    this.SetInlineText(node, input, begin, end, normalizeCode: true);
                    position = closing + runLength;
                }
                else
                {
                    this.AddInlineText(parent, input, position, runEnd);
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
                var beforeSpace = MarkdownUnicode.IsWhitespace(before);
                var afterSpace = MarkdownUnicode.IsWhitespace(after);
                var beforePunctuation = MarkdownUnicode.IsPunctuation(before);
                var afterPunctuation = MarkdownUnicode.IsPunctuation(after);
                var left = !afterSpace && (!afterPunctuation || beforeSpace || beforePunctuation);
                var right = !beforeSpace && (!beforePunctuation || afterSpace || afterPunctuation);
                var node = this.AddInlineText(parent, input, position, runEnd);
                var delimiter = this.delimiters.Add(new() { Node = node, Previous = this.lastDelimiter, Character = character, Count = runEnd - position, CanOpen = left && (character == '*' || !right || beforePunctuation), CanClose = right && (character == '*' || !left || afterPunctuation), });
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
                        var lastChild = this.nodes[parent].Last;
                        this.nodes[node].Kind = DocumentationMarkdownKind.Link;
                        this.nodes[node].End = this.MapEnd(end);
                        this.nodes[node].TextLength = 0;
                        this.nodes[node].Argument = this.values.Add(destination);
                        this.values.Add(title ?? string.Empty);
                        if (title is not null)
                        {
                            this.nodes[node].Flags |= MarkdownNodeFlags.HasTitle;
                        }

                        this.nodes[node].First = firstChild;
                        this.nodes[node].Last = firstChild == 0 ? 0 : lastChild;
                        this.nodes[node].Next = 0;
                        this.nodes[parent].Last = node;
                        if (firstChild != 0)
                        {
                            this.nodes[firstChild].Previous = 0;
                            for (var child = firstChild; child != 0; child = this.nodes[child].Next)
                            {
                                this.nodes[child].Parent = node;
                            }
                        }

                        lastLinkStart = bracket.Position;
                        position = end;
                        continue;
                    }
                }

                this.AddInlineText(parent, input, position, position + 1);
                position++;
            }
            else if (character == '<' && TryAutoLink(input, position, out var autoEnd, out var email))
            {
                var node = this.AddNode(DocumentationMarkdownKind.AutoLink, parent, this.MapStart(position), this.MapEnd(autoEnd));
                var destination = input[(position + 1)..(autoEnd - 1)].ToString();
                this.nodes[node].Argument = this.values.Add(email ? "mailto:" + destination : destination);
                this.SetInlineText(node, input, position + 1, autoEnd - 1);
                lastLinkStart = position;
                position = autoEnd;
            }
            else
            {
                this.AddInlineText(parent, input, position, position + 1);
                position++;
            }
        }

        this.ProcessEmphasis(0);
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

            var bucket = (close.Character == '*' ? 0 : 6) + (close.CanOpen ? 3 : 0) + (close.Count % 3);
            var opener = close.Previous;
            while (opener > openersBottom[bucket])
            {
                var open = this.delimiters[opener];
                if (open.CanOpen && open.Character == close.Character && (!(open.CanClose || close.CanOpen) || (open.Count + close.Count) % 3 != 0 || (open.Count % 3 == 0 && close.Count % 3 == 0)))
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
            var last = this.nodes[closeNode].Previous;
            var parent = this.nodes[openNode].Parent;
            // Allocate without linking at the tail: the wrapper replaces the enclosed sibling range.
            var wrapper = this.nodes.Add(new() { Kind = use == 2 ? DocumentationMarkdownKind.Strong : DocumentationMarkdownKind.Emphasis, Parent = parent, Previous = openNode, Next = closeNode, First = first, Last = last, Start = this.nodes[openNode].End - use, End = this.nodes[closeNode].Start + use, });
            this.nodes[openNode].Next = wrapper;
            this.nodes[closeNode].Previous = wrapper;
            this.nodes[first].Previous = 0;
            this.nodes[last].Next = 0;
            var nesting = 1;
            for (var child = first; child != 0; child = this.nodes[child].Next)
            {
                this.nodes[child].Parent = wrapper;
                if (this.nodes[child].Kind is DocumentationMarkdownKind.Emphasis or DocumentationMarkdownKind.Strong)
                {
                    nesting = Math.Max(nesting, this.nodes[child].Argument + 1);
                }
            }

            this.nodes[wrapper].Argument = nesting;
            if (nesting > this.maximumDepth)
            {
                throw new DocumentationMarkdownLimitException(this.maximumDepth);
            }

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
        var id = this.AddNode(DocumentationMarkdownKind.Text, parent, this.MapStart(start), this.MapEnd(end));
        this.SetInlineText(id, input, start, end);
        return id;
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

    private void SetInlineText(int id, ReadOnlySpan<char> input, int start, int end, bool normalizeCode = false)
    {
        var value = input[start..end];
        var sourceStart = this.MapStart(start);
        if ((!normalizeCode || !value.Contains('\n')) && !value.Contains('\0') && (end == start || this.MapEnd(end) - sourceStart == end - start))
        {
            this.nodes[id].TextStart = sourceStart;
            this.nodes[id].TextLength = end - start;
        }
        else
        {
            var decoded = value.ToString();
            if (normalizeCode)
            {
                decoded = decoded.Replace('\n', ' ');
            }

            this.SetValue(id, decoded.Replace('\0', '\uFFFD'));
        }
    }

    private int MapStart(int position) => this.mappedInline ? this.inlinePositions![position] : this.inlineBase + position;

    private int MapEnd(int end) => end == 0 ? this.inlineBase : this.MapStart(end - 1) + 1;

    private void IndexCodeRuns(ReadOnlySpan<char> input)
    {
        var position = 0;
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

            this.codeRuns.Add(new(start, position - start, -1));
        }

        for (var i = this.codeRuns.Count - 1; i >= 0; i--)
        {
            ref var run = ref this.codeRuns[i];
            if (run.Length <= 2)
            {
                ref var head = ref (run.Length == 1 ? ref this.singleCodeHead : ref this.doubleCodeHead);
                run.Next = head;
                head = i;
            }
            else
            {
                this.codeHeads ??= new();
                run.Next = this.codeHeads.GetValueOrDefault(run.Length, -1);
                this.codeHeads[run.Length] = i;
            }
        }
    }

    private int FindCodeCloser(int length, int after)
    {
        var index = length == 1 ? this.singleCodeHead : length == 2 ? this.doubleCodeHead : this.codeHeads?.GetValueOrDefault(length, -1) ?? -1;

        while (index >= 0 && this.codeRuns[index].Start < after)
        {
            index = this.codeRuns[index].Next;
        }

        if (length == 1)
        {
            this.singleCodeHead = index;
        }
        else if (length == 2)
        {
            this.doubleCodeHead = index;
        }
        else if (this.codeHeads is not null)
        {
            this.codeHeads[length] = index;
        }
        return index < 0 ? -1 : this.codeRuns[index].Start;
    }

    private void IndexParentheses(ReadOnlySpan<char> input)
    {
        EnsureBuffer(ref this.parentheses, input.Length);
        EnsureBuffer(ref this.nextDestinationStop, input.Length + 1);
        this.parentheses.AsSpan(0, input.Length).Fill(-1);
        this.nextDestinationStop.AsSpan(0, input.Length).Clear();
        MarkdownBuffer<int> stack = default;
        try
        {
            for (var i = 0; i < input.Length; i++)
            {
                if ((i & 1023) == 0)
                {
                    this.cancellationToken.ThrowIfCancellationRequested();
                }

                if (input[i] <= ' ' || input[i] is '<' or '\u007F')
                {
                    this.nextDestinationStop![i] = 1;
                }

                if (input[i] == '\\' && i + 1 < input.Length && MarkdownUnicode.IsAsciiPunctuation(input[i + 1]))
                {
                    i++;
                }
                else if (input[i] == '(')
                {
                    stack.Add(i);
                }
                else if (input[i] == ')' && stack.Count > 0)
                {
                    this.parentheses![stack[--stack.Count]] = i;
                }
            }
        }
        finally
        {
            stack.Dispose();
        }

        var stop = input.Length;
        this.nextDestinationStop![input.Length] = stop;
        for (var i = input.Length - 1; i >= 0; i--)
        {
            if (this.nextDestinationStop[i] != 0)
            {
                stop = i;
            }

            this.nextDestinationStop[i] = stop;
        }
    }

    private bool TryLink(ReadOnlySpan<char> input, int start, ref int failedDouble, ref int failedSingle, ref int failedParen, out string destination, out string? title, out int end)
    {
        destination = string.Empty;
        title = null;
        end = start;
        var position = start;
        SkipLinkWhitespace(input, ref position);
        var destinationStart = position;
        var destinationEnd = position;
        var emptyWithTitle = position > start && position < input.Length && input[position] is '\'' or '"';
        if (emptyWithTitle)
        {
        }
        else if (position < input.Length && input[position] == '<')
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
            while (position < input.Length && input[position] > ' ' && input[position] is not ('<' or ')' or '\u007F'))
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
                    if (close < 0 || close >= this.nextDestinationStop![position])
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

        var beforeWhitespace = emptyWithTitle ? start : position;
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

        destination = this.Decode(input[destinationStart..destinationEnd]);
        if (hasTitle)
        {
            title = this.Decode(input[titleStart..titleEnd]);
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
        while (end < input.Length && input[end] > ' ' && input[end] is not ('<' or '>'))
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

    private string Decode(ReadOnlySpan<char> input)
    {
        var special = input.IndexOfAny('\\', '&', '\0');
        if (special < 0)
        {
            return input.ToString();
        }

        var builder = new StringBuilder(input.Length);
        builder.Append(input[..special]);
        for (var i = special; i < input.Length; i++)
        {
            if (input[i] == '\\' && i + 1 < input.Length && MarkdownUnicode.IsAsciiPunctuation(input[i + 1]))
            {
                builder.Append(input[++i]);
            }
            else if (input[i] == '&' && TryEntity(input[i..], out var scalar, out var consumed))
            {
                if (scalar <= char.MaxValue)
                {
                    builder.Append((char)scalar);
                }
                else
                {
                    builder.Append(char.ConvertFromUtf32(scalar));
                }

                i += consumed - 1;
            }
            else
            {
                builder.Append(input[i] == '\0' ? '\uFFFD' : input[i]);
            }
        }

        return builder.ToString();
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

    private void DisposeInlineBuffers()
    {
        this.delimiters.Dispose();
        this.brackets.Dispose();
        this.codeRuns.Dispose();
        if (this.inlineCharacters is not null)
        {
            ArrayPool<char>.Shared.Return(this.inlineCharacters);
        }

        if (this.inlinePositions is not null)
        {
            ArrayPool<int>.Shared.Return(this.inlinePositions);
        }

        if (this.parentheses is not null)
        {
            ArrayPool<int>.Shared.Return(this.parentheses);
        }

        if (this.nextDestinationStop is not null)
        {
            ArrayPool<int>.Shared.Return(this.nextDestinationStop);
        }
    }

    private struct Delimiter
    {
        internal int Node;

        internal int Previous;

        internal int Next;

        internal int Count;

        internal char Character;

        internal bool CanOpen;

        internal bool CanClose;
    }

    private readonly record struct Bracket(int Node, int Position, int Delimiter, int Previous);

    private struct CodeRun(int start, int length, int next)
    {
        internal int Start = start;

        internal int Length = length;

        internal int Next = next;
    }
}
