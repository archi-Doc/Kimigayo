// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Documentation;

internal static class MarkdownUnicode
{
    // CommonMark whitespace is Zs plus TAB, LF, FF, CR. Neither VT nor NEL is whitespace.
    internal static bool IsWhitespace(int scalar) => scalar < 0 || scalar is 0x09 or 0x0A or 0x0C or 0x0D or 0x20 or 0xA0 or 0x1680 or (>= 0x2000 and <= 0x200A) or 0x202F or 0x205F or 0x3000;

    internal static bool IsAsciiPunctuation(char value) => value is (>= '!' and <= '/') or (>= ':' and <= '@') or (>= '[' and <= '`') or (>= '{' and <= '~');

    internal static bool IsPunctuation(int scalar)
    {
        if (scalar < 128)
        {
            return scalar >= 0 && IsAsciiPunctuation((char)scalar);
        }

        var ranges = MarkdownUnicodeData.Punctuation;
        var low = 0;
        var high = (ranges.Length / 2) - 1;
        while (low <= high)
        {
            var middle = (low + high) >> 1;
            var offset = middle * 2;
            if (scalar < ranges[offset])
            {
                high = middle - 1;
            }
            else if (scalar > ranges[offset + 1])
            {
                low = middle + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    internal static int Before(ReadOnlySpan<char> text, int offset)
    {
        if (offset == 0)
        {
            return -1;
        }

        var last = text[offset - 1];
        return char.IsLowSurrogate(last) && offset >= 2 && char.IsHighSurrogate(text[offset - 2]) ? char.ConvertToUtf32(text[offset - 2], last) : last;
    }

    internal static int After(ReadOnlySpan<char> text, int offset)
    {
        if (offset == text.Length)
        {
            return -1;
        }

        var first = text[offset];
        return char.IsHighSurrogate(first) && offset + 1 < text.Length && char.IsLowSurrogate(text[offset + 1]) ? char.ConvertToUtf32(first, text[offset + 1]) : first;
    }
}
