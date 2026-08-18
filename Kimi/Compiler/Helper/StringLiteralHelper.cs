// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Helper;

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
    private const char InvalidEscapeFallbackChar = 'k';
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

    /// <summary>
    /// Gets the decoded value of a validated string literal.
    /// </summary>
    /// <param name="rawLiteral">
    /// For an escaped string literal, the content after its surrounding double
    /// quotes have already been removed. For a raw string literal, the complete
    /// literal including its matching delimiters.
    /// </param>
    /// <param name="koto">
    /// The syntax node to which diagnostics for invalid escape sequences are added,
    /// or <see langword="null"/> to suppress diagnostics.
    /// </param>
    /// <returns>
    /// The decoded string value with escape sequences processed and raw-string
    /// delimiters removed.
    /// </returns>
    /// <remarks>
    /// Invalid escape sequences are replaced with fallback characters so that a
    /// string value can still be produced.
    /// </remarks>
    public static string GetStringLiteralValue(string rawLiteral, Koto? koto = default)
    {
        var span = rawLiteral.AsSpan();

        if (span.IsEmpty)
        {
            return string.Empty;
        }

        // The delimiters of an escaped string literal have already been removed.
        if (span[0] != '"')
        {
            var firstBackslash = span.IndexOf('\\');

            if (firstBackslash < 0)
            {
                return rawLiteral;
            }

            var decodedLength = firstBackslash + GetDecodedLength(span.Slice(firstBackslash), koto);

            return string.Create(
                decodedLength,
                new DecodeState(rawLiteral, firstBackslash),
                static (destination, state) =>
                {
                    Decode(state.Source, state.FirstBackslash, destination);
                });
        }

        // Raw string literal: """Text"""
        var delimiterLength = 1;
        while (delimiterLength < span.Length && span[delimiterLength] == '"')
        {
            delimiterLength++;
        }

        // An all-quote literal contains two equal delimiters:
        // """"""   -> 3 + 3
        // """""""" -> 4 + 4
        if (delimiterLength == span.Length)
        {
            delimiterLength >>= 1;
        }

        var contentLength = rawLiteral.Length - (delimiterLength << 1);
        if (contentLength <= 0)
        {
            return string.Empty;
        }

        return rawLiteral.Substring(delimiterLength, contentLength);
    }

    private static int GetDecodedLength(ReadOnlySpan<char> span, Koto? koto)
    {
        var length = 0;

        while (!span.IsEmpty)
        {
            var backslashIndex = span.IndexOf('\\');

            if (backslashIndex < 0)
            {
                return length + span.Length;
            }

            length += backslashIndex;
            span = span.Slice(backslashIndex + 1);
            if (span.IsEmpty)
            {
                // A trailing backslash is replaced with one fallback character.
                koto?.AddDiagnostic(KimiDiagnostic.UnsupportedEscape_Kd, '\\');
                return length + 1;
            }

            var escape = span[0];
            span = span.Slice(1);
            switch (escape)
            {
                case '0':
                case '\\':
                case 'e':
                case 't':
                case 'n':
                case '"':
                case '\'':
                    length++;
                    break;

                case 'u':
                    var succeeded = TryReadUnicodeEscape(ref span, koto, out var scalar);
                    length += succeeded && scalar > 0xFFFF ? 2 : 1;

                    break;

                default:
                    koto?.AddDiagnostic(KimiDiagnostic.UnsupportedEscape_Kd, escape);
                    length++;
                    break;
            }
        }

        return length;
    }

    private static void Decode(string source, int firstBackslash, Span<char> destination)
    {
        var span = source.AsSpan();
        var destinationIndex = firstBackslash;

        span.Slice(0, firstBackslash).CopyTo(destination);

        span = span.Slice(firstBackslash);
        while (!span.IsEmpty)
        {
            var backslashIndex = span.IndexOf('\\');
            if (backslashIndex < 0)
            {
                span.CopyTo(destination.Slice(destinationIndex));
                destinationIndex += span.Length;
                break;
            }

            if (backslashIndex > 0)
            {
                span.Slice(0, backslashIndex).CopyTo(destination.Slice(destinationIndex));
                span = span.Slice(backslashIndex);
                destinationIndex += backslashIndex;
            }

            // Skip '\'.
            span = span.Slice(1);
            if (span.IsEmpty)
            {
                destination[destinationIndex++] = InvalidEscapeFallbackChar;

                break;
            }

            var escape = span[0];
            span = span.Slice(1);
            switch (escape)
            {
                case '0':
                    destination[destinationIndex++] = '\0';
                    break;

                case '\\':
                    destination[destinationIndex++] = '\\';
                    break;

                case 'e':
                    destination[destinationIndex++] = '\u001b';
                    break;

                case 't':
                    destination[destinationIndex++] = '\t';
                    break;

                case 'n':
                    destination[destinationIndex++] = '\n';
                    break;

                case '"':
                    destination[destinationIndex++] = '"';
                    break;

                case '\'':
                    destination[destinationIndex++] = '\'';
                    break;

                case 'u':
                    if (!TryReadUnicodeEscape(ref span, default, out var scalar))
                    {
                        destination[destinationIndex++] = InvalidEscapeFallbackChar;

                        break;
                    }

                    if (scalar <= 0xFFFF)
                    {
                        destination[destinationIndex++] = (char)scalar;
                    }
                    else
                    {
                        scalar -= 0x10000;
                        destination[destinationIndex++] = (char)(0xD800 + (scalar >> 10));
                        destination[destinationIndex++] = (char)(0xDC00 + (scalar & 0x3FF));
                    }

                    break;

                default:
                    destination[destinationIndex++] = InvalidEscapeFallbackChar;

                    break;
            }
        }

        Debug.Assert(destinationIndex == destination.Length);
    }

    private static bool TryReadUnicodeEscape(ref ReadOnlySpan<char> span, Koto? koto, out uint scalar)
    {
        scalar = 0;
        if (span.IsEmpty || span[0] != '(')
        {
            koto?.AddDiagnostic(KimiDiagnostic.InvalidUnicodeEscape_Kd);
            return false;
        }

        span = span.Slice(1);

        uint value = 0;
        var digitCount = 0;
        var isValid = true;

        while (!span.IsEmpty)
        {
            var c = span[0];
            span = span.Slice(1);
            if (c == ')')
            {
                if (digitCount == 0 || !isValid)
                {
                    koto?.AddDiagnostic(KimiDiagnostic.InvalidUnicodeEscape_Kd);

                    return false;
                }

                if (value > 0x10FFFF ||
                    value is >= 0xD800 and <= 0xDFFF)
                {
                    koto?.AddDiagnostic(KimiDiagnostic.InvalidUnicodeScalar_Kd);

                    return false;
                }

                scalar = value;
                return true;
            }

            var digit = GetHexValue(c);
            if (digit < 0 || digitCount >= 6)
            {
                isValid = false;
            }
            else if (isValid)
            {
                value = (value << 4) | (uint)digit;
            }

            digitCount++;
        }

        koto?.AddDiagnostic(KimiDiagnostic.InvalidUnicodeEscape_Kd);

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetHexValue(char c)
    {
        var value = (uint)c;

        if (value - '0' <= 9)
        {
            return (int)(value - '0');
        }

        value = (value | 0x20) - 'a';

        return value <= 5 ? (int)value + 10 : -1;
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

    private readonly struct DecodeState
    {
        public readonly string Source;
        public readonly int FirstBackslash;

        public DecodeState(string source, int firstBackslash)
        {
            this.Source = source;
            this.FirstBackslash = firstBackslash;
        }
    }
}
