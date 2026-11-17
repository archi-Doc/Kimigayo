// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Helper;

namespace Kimi.Documentation;

internal static class MarkdownUnicode
{
    // CommonMark's character classes (2.1), each a generated row of MarkdownUnicodeData (Unicode 15.0.0) read above ASCII: whitespace
    // is Zs plus TAB, LF, FF and CR (neither VT nor NEL), punctuation the P and S categories. Outside the text (scalar < 0) is
    // whitespace, not punctuation. Above the Whitespace row's last scalar (U+3000) one compare answers, as for kana and Kanji.
    internal static bool IsWhitespace(int scalar)
        => scalar < 128
            ? scalar is < 0 or 0x20 or (>= 0x09 and <= 0x0D and not 0x0B)
            : scalar <= MarkdownUnicodeData.WhitespaceLast && InRanges(MarkdownUnicodeData.Whitespace, scalar);

    internal static bool IsAsciiPunctuation(char value) => value is (>= '!' and <= '/') or (>= ':' and <= '@') or (>= '[' and <= '`') or (>= '{' and <= '~');

    internal static bool IsPunctuation(int scalar)
        => scalar < 128 ? scalar >= 0 && IsAsciiPunctuation((char)scalar) : InRanges(MarkdownUnicodeData.Punctuation, scalar);

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

    // Whether a scalar lies in one of a row's merged inclusive (first, last) ranges, ascending, at least one: a search with a fixed
    // number of steps for the row's size and no branch on the data. The last range whose first is at most the scalar (else range 0)
    // lies in [low, low + count); each step moves low by half exactly when the probed first is at most the scalar, by the sign of
    // their difference (scalars, so no overflow). The scalar is then in that range when its distance from the first, unsigned, is
    // at most the range's.
    private static bool InRanges(ReadOnlySpan<byte> row, int scalar)
    {
        var ranges = new IntegerTable<int>(row);
        var low = 0;
        for (var count = ranges.Length / 2; count > 1; count -= count >> 1)
        {
            var half = count >> 1;
            low += half & ((ranges[(low + half) * 2] - scalar - 1) >> 31);
        }

        var first = ranges[low * 2];
        return (uint)(scalar - first) <= (uint)(ranges[(low * 2) + 1] - first);
    }
}
