// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Runtime.CompilerServices;

namespace Kimi.Compiler;

public enum ScanStringLiteralResult
{
    None,
    Invalid,
    StringLiteral,
    Interpolation,
    MultilineInterpolation,
}

public class StringLiteralHelper
{
    private static readonly SearchValues<char> SlashOrDoubleQuote = SearchValues.Create("\\\"");

    public static ScanStringLiteralResult ScanStringLiteral(ReadOnlySpan<char> text, out int doubleQuoteCount, out int stringLiteralLength)
    {
        doubleQuoteCount = CountDoubleQuotes(text);
        if (doubleQuoteCount == 0)
        {// Not string literal
            stringLiteralLength = 0;
            return ScanStringLiteralResult.None;
        }
        else if (doubleQuoteCount == 1)
        {// "Text with escape"
            doubleQuoteCount = 1;
            return ScanEscapedStringLiteral(text, out stringLiteralLength);
        }
        else if (doubleQuoteCount == 2)
        {// ""
            doubleQuoteCount = 1;
            stringLiteralLength = 2;
            return ScanStringLiteralResult.StringLiteral;
        }
        else
        {// """Text without escape"""
            return ScanRawStringLiteral(text, doubleQuoteCount, out stringLiteralLength);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ScanStringLiteralResult ScanInvalidStringLiteral(ReadOnlySpan<char> span, int quoteCount, out int stringLiteralLength)
    {
        // The closing delimiter is missing.
        // Returns the text up to the end of the input or the first delimiter.
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

        if (span[delimiterIndex] == '\"')
        {// Text"
            if (span[0] == BaseHelper.LfChar || span[0] == BaseHelper.CrChar)
            {// Multi-line string
                var spaceIndent = CountIndentSpace(span.Slice(0, delimiterIndex - 1));
                if (spaceIndent < 0)
                {// Treated as invalid because the text to the left of the closing delimiter contains non-whitespace characters.
                    stringLiteralLength = 2;
                    if (span.Length > 1 && span[1] == BaseHelper.LfChar)
                    {// CrLf
                        stringLiteralLength++;
                    }

                    return ScanStringLiteralResult.Invalid;
                }
            }

            stringLiteralLength = delimiterIndex + 1;
        }
        else
        {// text\(
            stringLiteralLength = delimiterIndex + 2;
        }

        return (span[0] == BaseHelper.LfChar || span[0] == BaseHelper.CrChar) ?
            ScanStringLiteralResult.MultilineInterpolation :
            ScanStringLiteralResult.Interpolation;
    }

    private static ScanStringLiteralResult ScanRawStringLiteral(ReadOnlySpan<char> text, int doubleQuoteCount, out int stringLiteralLength)
    {
        var span = text.Slice(doubleQuoteCount); // Text
        var closingDelimiterIndex = span.IndexOf(text.Slice(0, doubleQuoteCount), StringComparison.InvariantCulture);

        if (closingDelimiterIndex < 0)
        {
            return ScanInvalidStringLiteral(span, doubleQuoteCount, out stringLiteralLength);
        }

        if (span[0] == BaseHelper.LfChar || span[0] == BaseHelper.CrChar)
        {// Multi-line string
            var indentSpan = CountIndentSpace(span.Slice(0, closingDelimiterIndex - 1));
            if (indentSpan < 0)
            {// Treated as invalid because the text to the left of the closing delimiter contains non-whitespace characters.
                stringLiteralLength = doubleQuoteCount + 1;
                if (span.Length > 1 && span[1] == BaseHelper.LfChar)
                {// CrLf
                    stringLiteralLength++;
                }

                return ScanStringLiteralResult.Invalid;
            }
        }

        stringLiteralLength = doubleQuoteCount + closingDelimiterIndex + doubleQuoteCount;
        return ScanStringLiteralResult.StringLiteral;
    }

    private static int CountIndentSpace(ReadOnlySpan<char> text)
    {
        var index = text.Length - 1;
        while (index >= 0)
        {
            var c = text[index];
            if (c == Constants.SpaceChar)
            {
                index--;
            }
            else if (c == BaseHelper.LfChar)
            {
                break;
            }
            else
            {
                return -1;
            }
        }

        return text.Length - 1 - index;
    }

    private static int IndexOfInterpolationOrUnescapedQuote(ReadOnlySpan<char> text)
    {
        var offset = 0;
        while ((uint)offset < (uint)text.Length)
        {// /( or " (except \")
            var relativeIndex = text[offset..].IndexOfAny(SlashOrDoubleQuote);
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
    private static int CountDoubleQuotes(ReadOnlySpan<char> text)
    {
        return text.Slice(0, text.Length >> 1).IndexOfAnyExcept('"');
    }
}
