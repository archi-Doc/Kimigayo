// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Documentation;

#pragma warning disable SA1201, SA1202, SA1204, SA1401, SA1513, SA1600 // Parser records beside their construct.

// Autolinks (CommonMark 6.5): an absolute URI or an email address between '<' and '>'.
internal ref partial struct DocumentationMarkdownParser
{
    private int ParseAutoLink(int parent, ReadOnlySpan<char> input, int position)
    {
        var close = position + 1;
        while (close < input.Length && (input[close] > ' ' || input[close] == '\0') && input[close] is not ('<' or '>'))
        {
            close++;
        }

        if (close == input.Length || input[close] != '>')
        {
            return position;
        }

        var target = input[(position + 1)..close];
        var email = !IsAbsoluteUri(target);
        if (email && !IsEmailAddress(target))
        {
            return position;
        }

        var end = close + 1;
        var node = this.AddNode(DocumentationMarkdownKind.AutoLink, parent, this.MapStart(position), this.MapEnd(end));
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
            this.nodes[node].Argument = this.values.Add(
                email ? new(string.Concat("mailto:", target)) : this.SliceValue(input, position + 1, close));
            this.SetInlineText(node, input, position + 1, close, this.MapStart(position + 1), this.MapEnd(close));
        }

        this.lastLinkStart = position;
        return end;
    }

    // A scheme of 2 to 32 characters (a letter, then letters, digits, '+', '-' or '.') and a colon.
    private static bool IsAbsoluteUri(ReadOnlySpan<char> value)
    {
        var colon = value.IndexOf(':');
        if (colon is < 2 or > 32 || !IsAsciiLetter(value[0]))
        {
            return false;
        }

        var valid = true;
        for (var i = 1; i < colon; i++)
        {
            valid &= IsAsciiLetter(value[i]) || char.IsAsciiDigit(value[i]) || value[i] is '+' or '-' or '.';
        }

        return valid;
    }

    // A local part of the permitted characters, '@', and dot-separated labels of 1 to 63 letters, digits or inner hyphens.
    private static bool IsEmailAddress(ReadOnlySpan<char> value)
    {
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

        return true;
    }

    private static bool IsAsciiLetter(char value) => (uint)((value | 0x20) - 'a') <= 'z' - 'a';
}
