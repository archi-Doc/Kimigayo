// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Kimi.Compiler;

public enum ScanStringLiteralResult : byte
{
    None,
    Invalid,
    String,
    MultilineString,
    Interpolation,
    MultilineInterpolation,
}

public static class StringLiteralHelper
{
    private static readonly SearchValues<char> BackslashOrDoubleQuote = SearchValues.Create("\\\"");

    public static ScanStringLiteralResult ScanStringLiteral(ReadOnlySpan<char> text, out int doubleQuoteCount, out int stringLiteralLength)
    {
        doubleQuoteCount = CountLeadingDoubleQuotes(text);
        if (doubleQuoteCount == 0)
        {// Not string literal
            stringLiteralLength = 0;
            return ScanStringLiteralResult.None;
        }
        else if (doubleQuoteCount == 1)
        {// "Text with escape"
            return ScanEscapedStringLiteral(text, out stringLiteralLength);
        }
        else if (doubleQuoteCount == 2)
        {// ""
            doubleQuoteCount = 1;
            stringLiteralLength = 2;
            return ScanStringLiteralResult.String;
        }
        else
        {// """Text without escape"""
            return ScanRawStringLiteral(text, doubleQuoteCount, out stringLiteralLength);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ScanStringLiteralResult ScanInvalidStringLiteral(ReadOnlySpan<char> span, int quoteCount, out int stringLiteralLength)
    {
        // Consumes through the first LF, including a preceding CR when present.
        // If no LF exists, consumes only the opening delimiter.
        var linebreakIndex = span.IndexOf(BaseHelper.LfChar);
        if (linebreakIndex >= 0)
        {// Lf, CrLf
            stringLiteralLength = quoteCount + linebreakIndex + 1;
        }
        else
        {
            stringLiteralLength = quoteCount;
        }

        return ScanStringLiteralResult.Invalid;
    }

    private static ScanStringLiteralResult ScanEscapedStringLiteral(ReadOnlySpan<char> text, out int stringLiteralLength)
    {
        var span = text.Slice(1);
        var delimiterIndex = IndexOfInterpolationOrUnescapedQuote(span);
        if (delimiterIndex < 0)
        {
            return ScanInvalidStringLiteral(span, 1, out stringLiteralLength);
        }

        var isMultiline = StartsWithLineBreak(span);
        if (!isMultiline && span[..delimiterIndex].Contains(BaseHelper.LfChar))
        {
            return ScanInvalidStringLiteral(span, 1, out stringLiteralLength);
        }

        if (span[delimiterIndex] == '"')
        {// Text"
            if (isMultiline && !HasValidClosingDelimiterIndent(span[..delimiterIndex]))
            {// Treated as invalid because the text to the left of the closing delimiter contains non-whitespace characters.
                return ScanInvalidStringLiteral(span, 1, out stringLiteralLength);
            }

            stringLiteralLength = delimiterIndex + 2;

            return isMultiline ?
                ScanStringLiteralResult.MultilineString :
                ScanStringLiteralResult.String;
        }
        else
        {// text\(
            stringLiteralLength = delimiterIndex + 3;

            return isMultiline ?
                ScanStringLiteralResult.MultilineInterpolation :
                ScanStringLiteralResult.Interpolation;
        }
    }

    private static ScanStringLiteralResult ScanRawStringLiteral(ReadOnlySpan<char> text, int doubleQuoteCount, out int stringLiteralLength)
    {
        var span = text.Slice(doubleQuoteCount); // Text
        var delimiterIndex = span.IndexOf(text[..doubleQuoteCount]);
        if (delimiterIndex < 0)
        {
            return ScanInvalidStringLiteral(span, doubleQuoteCount, out stringLiteralLength);
        }

        var isMultiline = StartsWithLineBreak(span);
        if (!isMultiline && span[..delimiterIndex].Contains(BaseHelper.LfChar))
        {
            return ScanInvalidStringLiteral(span, doubleQuoteCount, out stringLiteralLength);
        }

        var i = delimiterIndex + doubleQuoteCount;
        while (i < span.Length && span[i] == '"')
        {
            i++;
            delimiterIndex++;
        }

        if (isMultiline && !HasValidClosingDelimiterIndent(span[..delimiterIndex]))
        {// Treated as invalid because the text to the left of the closing delimiter contains non-whitespace characters.
            return ScanInvalidStringLiteral(span, doubleQuoteCount, out stringLiteralLength);
        }

        stringLiteralLength = doubleQuoteCount + delimiterIndex + doubleQuoteCount;
        return isMultiline ?
            ScanStringLiteralResult.MultilineString :
            ScanStringLiteralResult.String;
    }

    private static bool HasValidClosingDelimiterIndent(ReadOnlySpan<char> text)
    {
        var index = text.Length - 1;
        while (index >= 0)
        {
            var c = text[index];
            if (c == Constants.SpaceChar)
            {
                index--;
                continue;
            }

            return c == BaseHelper.LfChar;
        }

        return false;
    }

    private static int IndexOfInterpolationOrUnescapedQuote(ReadOnlySpan<char> text)
    {
        var offset = 0;
        while ((uint)offset < (uint)text.Length)
        {// \( or an unescaped "
            var relativeIndex = text[offset..].IndexOfAny(BackslashOrDoubleQuote);
            if (relativeIndex < 0)
            {
                return -1;
            }

            var index = offset + relativeIndex;
            if (text[index] == '"')
            {// A quotation mark without preceding backslashes.
                return index;
            }

            // Count a consecutive run of backslashes.
            var backslashStart = index;
            do
            {
                index++;
            }
            while ((uint)index < (uint)text.Length &&
                   text[index] == '\\');

            if ((uint)index >= (uint)text.Length)
            {
                return -1;
            }

            var backslashCount = index - backslashStart;
            var next = text[index];

            if ((backslashCount & 1) != 0)
            {
                // An odd number of backslashes means that the final backslash
                // introduces an escape sequence or string interpolation.
                if (next == '(')
                {
                    return index - 1;
                }

                if (next == '"')
                {// Escaped quotation mark.
                    offset = index + 1;
                    continue;
                }
            }
            else if (next == '"')
            {// An even number of backslashes leaves the quotation mark unescaped.
                return index;
            }

            offset = index + 1;
        }

        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountLeadingDoubleQuotes(ReadOnlySpan<char> text)
    {
        var index = 0;
        while ((uint)index < (uint)text.Length &&
               text[index] == '"')
        {
            index++;
        }

        return index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool StartsWithLineBreak(ReadOnlySpan<char> text)
    {
        return !text.IsEmpty &&
            (text[0] == BaseHelper.LfChar ||
            (text[0] == BaseHelper.CrChar && text.Length >= 2 && text[1] == BaseHelper.LfChar));
    }
}
