// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Helper;

namespace Kimi.Documentation;

internal static class MarkdownUnicode
{
    // CommonMark's character classes (2.1), each generated into MarkdownUnicodeData (Unicode 15.0.0) as a Latin-1 map read below 256
    // and a row of its ranges above U+00FF read between the row's first and last scalars, so a scalar outside them takes two compares
    // (for whitespace: Greek, Cyrillic and the rest below U+1680; kana, Kanji and the rest above U+3000). Whitespace is Zs plus TAB,
    // LF, FF and CR (neither VT nor NEL), punctuation the P and S categories. Outside the text (scalar < 0) is whitespace, not
    // punctuation.
    internal static bool IsWhitespace(int scalar)
        => scalar < 256
            ? scalar < 0 || InLatin1(MarkdownUnicodeData.WhitespaceLatin1, scalar)
            : scalar >= MarkdownUnicodeData.WhitespaceFirst && scalar <= MarkdownUnicodeData.WhitespaceLast
                && InRanges(MarkdownUnicodeData.Whitespace, scalar);

    internal static bool IsAsciiPunctuation(char value) => value is (>= '!' and <= '/') or (>= ':' and <= '@') or (>= '[' and <= '`') or (>= '{' and <= '~');

    internal static bool IsPunctuation(int scalar)
        => scalar < 256
            ? scalar >= 0 && InLatin1(MarkdownUnicodeData.PunctuationLatin1, scalar)
            : scalar >= MarkdownUnicodeData.PunctuationFirst && scalar <= MarkdownUnicodeData.PunctuationLast
                && InRanges(MarkdownUnicodeData.Punctuation, scalar);

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
        return char.IsHighSurrogate(first) && offset + 1 < text.Length && char.IsLowSurrogate(text[offset + 1])
            ? char.ConvertToUtf32(first, text[offset + 1])
            : first;
    }

    // Whether a scalar in 0..255 is in a class's Latin-1 map: bit (scalar % 64) of word (scalar / 64).
    private static bool InLatin1(ReadOnlySpan<byte> map, int scalar) => ((new IntegerTable<ulong>(map)[scalar >> 6] >> (scalar & 63)) & 1) != 0;

    // Whether a scalar lies in one of a row's merged inclusive (first, last) ranges, ascending: a binary search, inlined into both
    // classes, since the call, not the search, was the measured cost for neighbours inside a row's bounds (G818).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool InRanges(ReadOnlySpan<byte> row, int scalar)
    {
        var ranges = new IntegerTable<int>(row);
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
}
