// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Kimi.Compiler;

public enum ScanStringLiteralResult
{
    NotStringLiteral,
    StringLiteral,
    InvalidStringLiteral,
    StartInterpolation,
}

public class StringLiteralHelper
{
    public static ScanStringLiteralResult ScanStringLiteral(ReadOnlySpan<char> text, out int doubleQuoteCount, out int stringLiteralLength)
    {
        doubleQuoteCount = CountDoubleQuotes(text);
        if (doubleQuoteCount == 0)
        {
            stringLiteralLength = 0;
            return ScanStringLiteralResult.NotStringLiteral;
        }
        else if (doubleQuoteCount < 3)
        {
            // Two consecutive quotes represent an empty escaped string,
            // rather than a two-character delimiter.
            doubleQuoteCount = 1;
            return ScanEscapedStringLiteral(text, out stringLiteralLength);
        }
        else
        {
            return ScanRawStringLiteral(text, doubleQuoteCount, out stringLiteralLength);
        }
    }

    private static void ScanEscapedStringLiteral(
        ReadOnlySpan<char> text,
        out int stringLiteralLength,
        out int interpolationLength)
    {
        stringLiteralLength = -1;
        interpolationLength = -1;

        var index = 1;

        while (index < text.Length)
        {
            var c = text[index];

            if (c == '"')
            {
                stringLiteralLength = index + 1;
                return;
            }

            if (c != '\\')
            {
                index++;
                continue;
            }

            if (index + 1 >= text.Length)
            {
                // The literal ends with an incomplete escape sequence.
                return;
            }

            if (text[index + 1] == '(')
            {
                SetInterpolationLength(index, ref interpolationLength);

                if (!TrySkipInterpolation(text, index, out index))
                {
                    return;
                }

                continue;
            }

            // Escape validity is deliberately not checked here.
            //
            // This also prevents \" from terminating the string. An invalid
            // escape such as \q is skipped structurally in the same manner.
            index += 2;
        }
    }

    private static ScanStringLiteralResult ScanRawStringLiteral(ReadOnlySpan<char> text, int doubleQuoteCount, out int stringLiteralLength)
    {
        var span = text.Slice(doubleQuoteCount); // Text
        var closingDelimiterIndex = span.IndexOf(text.Slice(0, doubleQuoteCount), StringComparison.InvariantCulture);

        if (closingDelimiterIndex < 0)
        {
            // The closing delimiter is missing.
            // Returns the text up to the end of the input or the next line break.
            var linebreakIndex = span.IndexOf(BaseHelper.LfChar);
            if (linebreakIndex >= 0)
            {// Lf, CrLf
                stringLiteralLength = doubleQuoteCount + linebreakIndex + 1;
            }

            stringLiteralLength = text.Length;
            return ScanStringLiteralResult.InvalidStringLiteral;
        }

        if (span[0] == BaseHelper.LfChar || span[0] == BaseHelper.CrChar)
        {// Multi-line string
            var lastLinebreakIndex = span.Slice(0, closingDelimiterIndex).LastIndexOf(BaseHelper.LfChar);
            var indentSpan = span.Slice(lastLinebreakIndex + 1, closingDelimiterIndex);
            if (indentSpan.IndexOfAnyExcept(' ') >= 0)
            {// Treated as invalid because the text to the left of the closing delimiter contains non-whitespace characters.
                stringLiteralLength = doubleQuoteCount + 1;
                return ScanStringLiteralResult.InvalidStringLiteral;
            }
        }

        stringLiteralLength = doubleQuoteCount + closingDelimiterIndex + doubleQuoteCount;
        return ScanStringLiteralResult.StringLiteral;
    }

    private static void ScanSingleLineRawStringLiteral(
        ReadOnlySpan<char> text,
        int doubleQuoteCount,
        out int stringLiteralLength,
        out int interpolationLength)
    {
        stringLiteralLength = -1;
        interpolationLength = -1;

        var index = doubleQuoteCount;

        while (index < text.Length)
        {
            var c = text[index];

            if (c is '\r' or '\n')
            {
                // A raw literal containing a line break must use the
                // multiline delimiter layout.
                return;
            }

            if (c == '\\' &&
                index + 1 < text.Length &&
                text[index + 1] == '(')
            {
                SetInterpolationLength(index, ref interpolationLength);

                var interpolationStart = index;

                if (!TrySkipInterpolation(text, index, out index))
                {
                    return;
                }

                if (ContainsLineBreak(text[interpolationStart..index]))
                {
                    // The entire literal must remain on one physical line.
                    return;
                }

                continue;
            }

            if (c != '"')
            {
                index++;
                continue;
            }

            var closingQuoteCount = CountDoubleQuotes(text, index);

            if (closingQuoteCount == doubleQuoteCount)
            {
                stringLiteralLength = index + closingQuoteCount;
                return;
            }

            if (closingQuoteCount > doubleQuoteCount)
            {
                // The longest quote sequence at the end does not match
                // the opening delimiter.
                return;
            }

            // A shorter run of quotes is literal content.
            index += closingQuoteCount;
        }
    }

    private static void ScanMultilineRawStringLiteral(
        ReadOnlySpan<char> text,
        int doubleQuoteCount,
        int contentStart,
        out int stringLiteralLength,
        out int interpolationLength)
    {
        stringLiteralLength = -1;
        interpolationLength = -1;

        var index = contentStart;
        var lineStart = contentStart;

        while (index < text.Length)
        {
            var c = text[index];

            if (c == '\\' &&
                index + 1 < text.Length &&
                text[index + 1] == '(')
            {
                SetInterpolationLength(index, ref interpolationLength);

                var interpolationStart = index;

                if (!TrySkipInterpolation(text, index, out index))
                {
                    return;
                }

                UpdateLineStart(
                    text,
                    interpolationStart,
                    index,
                    ref lineStart);

                continue;
            }

            if (c == '"')
            {
                var closingQuoteCount = CountDoubleQuotes(text, index);

                if (closingQuoteCount >= doubleQuoteCount)
                {
                    // The closing delimiter must be preceded only by
                    // indentation on its physical line.
                    var indentation = text[lineStart..index];

                    if (closingQuoteCount != doubleQuoteCount ||
                        indentation.IndexOfAnyExcept(Constants.SpaceChar) >= 0 ||
                        !HasValidMultilineIndentation(
                            text,
                            contentStart,
                            lineStart,
                            indentation))
                    {
                        return;
                    }

                    stringLiteralLength = index + closingQuoteCount;
                    return;
                }

                index += closingQuoteCount;
                continue;
            }

            if (TryConsumeLineBreak(text, index, out var nextLineStart))
            {
                index = nextLineStart;
                lineStart = nextLineStart;
                continue;
            }

            index++;
        }
    }

    /// <summary>
    /// Skips an interpolation beginning with <c>\(</c>.
    /// </summary>
    /// <remarks>
    /// Parentheses are balanced, and nested string literals are skipped so
    /// that quotes and parentheses inside them do not affect the outer scan.
    /// </remarks>
    private static bool TrySkipInterpolation(
        ReadOnlySpan<char> text,
        int interpolationStart,
        out int nextIndex)
    {
        var index = interpolationStart + 2;
        var parenthesisDepth = 1;

        while (index < text.Length)
        {
            var c = text[index];

            if (c == '"')
            {
                if (!ScanStringLiteral(
                        text[index..],
                        out _,
                        out var nestedLiteralLength,
                        out _) ||
                    nestedLiteralLength < 0)
                {
                    nextIndex = text.Length;
                    return false;
                }

                index += nestedLiteralLength;
                continue;
            }

            if (c == '(')
            {
                parenthesisDepth++;
                index++;
                continue;
            }

            if (c == ')')
            {
                parenthesisDepth--;
                index++;

                if (parenthesisDepth == 0)
                {
                    nextIndex = index;
                    return true;
                }

                continue;
            }

            index++;
        }

        nextIndex = text.Length;
        return false;
    }

    private static bool HasValidMultilineIndentation(
        ReadOnlySpan<char> text,
        int contentStart,
        int closingLineStart,
        ReadOnlySpan<char> indentation)
    {
        var lineStart = contentStart;

        while (lineStart < closingLineStart)
        {
            var lineEnd = lineStart;

            while (lineEnd < closingLineStart &&
                   text[lineEnd] is not ('\r' or '\n'))
            {
                lineEnd++;
            }

            var line = text[lineStart..lineEnd];

            if (line.IndexOfAnyExcept(Constants.SpaceChar) < 0)
            {
                // For a blank line, either whitespace sequence may be a
                // prefix of the other. This permits empty and partially
                // indented blank lines without allowing mixed indentation.
                if (!IsPrefix(line, indentation) &&
                    !IsPrefix(indentation, line))
                {
                    return false;
                }
            }
            else if (!IsPrefix(indentation, line))
            {
                // Every nonblank line must begin with the indentation
                // established by the closing delimiter.
                return false;
            }

            if (lineEnd >= closingLineStart)
            {
                break;
            }

            if (!TryConsumeLineBreak(text, lineEnd, out lineStart))
            {
                return false;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountDoubleQuotes(ReadOnlySpan<char> text)
    {
        return text.Slice(0, text.Length >> 1).IndexOfAnyExcept('"');
    }

    private static void SetInterpolationLength(
        int interpolationStart,
        ref int interpolationLength)
    {
        if (interpolationLength < 0)
        {
            // Include both '\' and '('.
            interpolationLength = interpolationStart + 2;
        }
    }

    private static bool TryConsumeLineBreak(
        ReadOnlySpan<char> text,
        int index,
        out int nextIndex)
    {
        if ((uint)index >= (uint)text.Length)
        {
            nextIndex = index;
            return false;
        }

        if (text[index] == '\n')
        {
            nextIndex = index + 1;
            return true;
        }

        if (text[index] == '\r')
        {
            index++;

            if (index < text.Length && text[index] == '\n')
            {
                index++;
            }

            nextIndex = index;
            return true;
        }

        nextIndex = index;
        return false;
    }

    private static void UpdateLineStart(
        ReadOnlySpan<char> text,
        int start,
        int end,
        ref int lineStart)
    {
        var index = start;

        while (index < end)
        {
            if (text[index] == '\n')
            {
                lineStart = ++index;
                continue;
            }

            if (text[index] == '\r')
            {
                index++;

                if (index < end && text[index] == '\n')
                {
                    index++;
                }

                lineStart = index;
                continue;
            }

            index++;
        }
    }

    private static bool ContainsLineBreak(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (c is '\r' or '\n')
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPrefix(
        ReadOnlySpan<char> prefix,
        ReadOnlySpan<char> text)
    {
        if (prefix.Length > text.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (prefix[i] != text[i])
            {
                return false;
            }
        }

        return true;
    }
}
