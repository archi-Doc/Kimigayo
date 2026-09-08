// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Helper;

/// <summary>
/// Identifies the result of scanning a string literal.
/// </summary>
public enum ScanStringLiteralResult : byte
{
    /// <summary>No string literal was found.</summary>
    None,

    /// <summary>The string literal is malformed.</summary>
    Invalid,

    /// <summary>A single-line string literal was found.</summary>
    String,

    /// <summary>A multiline string literal was found.</summary>
    MultilineString,

    /// <summary>A single-line string containing interpolation was found.</summary>
    Interpolation,

    /// <summary>A multiline string containing interpolation was found.</summary>
    MultilineInterpolation,
}

/// <summary>
/// Provides methods for scanning and decoding string literals.
/// </summary>
public static class StringLiteralHelper
{
    private const char InvalidEscapeFallbackChar = 'k';
    private static readonly SearchValues<char> BackslashOrDoubleQuote = SearchValues.Create("\\\"");

    /// <summary>
    /// Scans a string literal at the start of the specified text.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="doubleQuoteCount">The detected opening quote count.</param>
    /// <param name="stringLiteralLength">The number of characters consumed.</param>
    /// <returns>The scan result.</returns>
    public static ScanStringLiteralResult ScanStringLiteral(ReadOnlySpan<char> text, out int doubleQuoteCount, out int stringLiteralLength)
    {
        doubleQuoteCount = CountLeadingDoubleQuotes(text);
        if (doubleQuoteCount == 0)
        {
            stringLiteralLength = 0;
            return ScanStringLiteralResult.None;
        }
        else if (doubleQuoteCount == 1)
        {
            return ScanEscapedStringLiteral(text, out stringLiteralLength);
        }
        else if (doubleQuoteCount == 2)
        {
            doubleQuoteCount = 1;
            stringLiteralLength = 2;
            return ScanStringLiteralResult.String;
        }
        else
        {
            return ScanRawStringLiteral(text, doubleQuoteCount, out stringLiteralLength);
        }
    }

    /// <summary>
    /// Decodes a validated string literal.
    /// </summary>
    /// <param name="rawLiteral">
    /// The unquoted content of an escaped literal, or the complete raw literal.
    /// </param>
    /// <param name="koto">
    /// The node that receives diagnostics, or <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The decoded string value.
    /// </returns>
    /// <remarks>
    /// Invalid escape sequences are replaced with fallback characters.
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

        // Count the opening delimiter of a raw string.
        var delimiterLength = 1;
        while (delimiterLength < span.Length && span[delimiterLength] == '"')
        {
            delimiterLength++;
        }

        // An all-quote literal consists of two equal delimiters.
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

    // The input starts with the interpolation's opening parenthesis. Quotes and comments
    // are scanned as units, so their parentheses do not affect the nesting depth.
    internal static int FindInterpolationEnd(ReadOnlySpan<char> text, int depth = 0)
    {
        if (depth >= 128)
        {
            return -1;
        }

        var parentheses = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                parentheses++;
            }
            else if (text[i] == ')' && --parentheses == 0)
            {
                return i;
            }
            else if (text[i] == '"')
            {
                var quotes = CountLeadingDoubleQuotes(text[i..]);
                int length;
                var result = quotes >= 3
                    ? ScanRawStringLiteral(text[i..], quotes, out length)
                    : quotes == 2
                        ? ScanStringLiteral(text[i..], out _, out length)
                        : ScanEscapedStringLiteral(text[i..], out length, depth + 1);
                if (result == ScanStringLiteralResult.Invalid)
                {
                    return -1;
                }

                i += length - 1;
            }
            else if (text[i] == '\'')
            {
                if (!CharLiteralHelper.Scan(text[i..], out var length))
                {
                    return -1;
                }

                i += length - 1;
            }
            else if (text[i] == '/' && i + 1 < text.Length)
            {
                if (text[i + 1] == '/')
                {
                    var end = text[(i + 2)..].IndexOfAny('\r', '\n');
                    if (end < 0)
                    {
                        return -1;
                    }

                    i += end + 1;
                }
                else if (text[i + 1] == '*')
                {
                    var end = text[(i + 2)..].IndexOf("*/");
                    if (end < 0)
                    {
                        return -1;
                    }

                    i += end + 3;
                }
            }
        }

        return -1;
    }

    // Input begins immediately after a backslash. Shared by char and escaped string
    // literals; interpolation is deliberately handled only by the string parser.
    internal static bool TryReadCharacterEscape(ref ReadOnlySpan<char> span, Koto? koto, out uint scalar)
    {
        scalar = 0;
        if (span.IsEmpty)
        {
            koto?.AddDiagnostic(DiagnosticCode.UnsupportedEscape_Kd, '\\');
            return false;
        }

        var escape = span[0];
        span = span[1..];
        if (escape == 'u')
        {
            return TryReadUnicodeEscape(ref span, koto, out scalar);
        }

        var value = escape switch
        {
            '0' => 0,
            '\\' => '\\',
            'e' => 0x1B,
            't' => '\t',
            'n' => '\n',
            'r' => '\r',
            '"' => '"',
            '\'' => '\'',
            _ => -1,
        };
        if (value < 0)
        {
            koto?.AddDiagnostic(DiagnosticCode.UnsupportedEscape_Kd, escape);
            return false;
        }

        scalar = (uint)value;
        return true;
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
            span = span[(backslashIndex + 1)..];
            var succeeded = TryReadCharacterEscape(ref span, koto, out var scalar);
            length += succeeded && scalar > 0xFFFF ? 2 : 1;
        }

        return length;
    }

    private static void Decode(string source, int firstBackslash, Span<char> destination)
    {
        var span = source.AsSpan();
        var destinationIndex = firstBackslash;
        span[..firstBackslash].CopyTo(destination);
        span = span[firstBackslash..];
        while (!span.IsEmpty)
        {
            var backslashIndex = span.IndexOf('\\');
            if (backslashIndex < 0)
            {
                span.CopyTo(destination[destinationIndex..]);
                destinationIndex += span.Length;
                break;
            }

            span[..backslashIndex].CopyTo(destination[destinationIndex..]);
            destinationIndex += backslashIndex;
            span = span[(backslashIndex + 1)..];
            if (!TryReadCharacterEscape(ref span, default, out var scalar))
            {
                destination[destinationIndex++] = InvalidEscapeFallbackChar;
            }
            else if (scalar <= 0xFFFF)
            {
                destination[destinationIndex++] = (char)scalar;
            }
            else
            {
                scalar -= 0x10000;
                destination[destinationIndex++] = (char)(0xD800 + (scalar >> 10));
                destination[destinationIndex++] = (char)(0xDC00 + (scalar & 0x3FF));
            }
        }

        Debug.Assert(destinationIndex == destination.Length);
    }

    private static bool TryReadUnicodeEscape(ref ReadOnlySpan<char> span, Koto? koto, out uint scalar)
    {
        scalar = 0;
        if (span.IsEmpty || span[0] != '(')
        {
            koto?.AddDiagnostic(DiagnosticCode.InvalidUnicodeEscape_Kd);
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
                    koto?.AddDiagnostic(DiagnosticCode.InvalidUnicodeEscape_Kd);

                    return false;
                }

                if (value > 0x10FFFF ||
                    value is >= 0xD800 and <= 0xDFFF)
                {
                    koto?.AddDiagnostic(DiagnosticCode.InvalidUnicodeScalar_Kd);

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

        koto?.AddDiagnostic(DiagnosticCode.InvalidUnicodeEscape_Kd);

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
        // Include the first line break when one is available.
        var linebreakIndex = span.IndexOf(BaseHelper.LfChar);
        if (linebreakIndex >= 0)
        {
            stringLiteralLength = quoteCount + linebreakIndex + 1;
        }
        else
        {
            stringLiteralLength = quoteCount;
        }

        return ScanStringLiteralResult.Invalid;
    }

    private static ScanStringLiteralResult ScanEscapedStringLiteral(ReadOnlySpan<char> text, out int stringLiteralLength, int depth = 0)
    {
        var offset = 1;
        var interpolated = false;
        while (offset < text.Length)
        {
            var relative = IndexOfInterpolationOrUnescapedQuote(text[offset..]);
            if (relative < 0)
            {
                break;
            }

            var delimiter = offset + relative;
            if (text[delimiter] == '"')
            {
                stringLiteralLength = delimiter + 1;
                var multiline = text[..delimiter].IndexOfAny('\r', '\n') >= 0;
                return interpolated
                    ? (multiline ? ScanStringLiteralResult.MultilineInterpolation : ScanStringLiteralResult.Interpolation)
                    : (multiline ? ScanStringLiteralResult.MultilineString : ScanStringLiteralResult.String);
            }

            var close = FindInterpolationEnd(text[(delimiter + 1)..], depth + 1);
            if (close < 0)
            {
                break;
            }

            interpolated = true;
            offset = delimiter + close + 2;
        }

        return ScanInvalidStringLiteral(text[1..], 1, out stringLiteralLength);
    }

    private static ScanStringLiteralResult ScanRawStringLiteral(ReadOnlySpan<char> text, int doubleQuoteCount, out int stringLiteralLength)
    {
        var span = text.Slice(doubleQuoteCount);
        var delimiterIndex = span.IndexOf(text[..doubleQuoteCount]);
        if (delimiterIndex < 0)
        {
            return ScanInvalidStringLiteral(span, doubleQuoteCount, out stringLiteralLength);
        }

        var isMultiline = span[..delimiterIndex].IndexOfAny('\r', '\n') >= 0;

        // Treat surplus quotes before the closing delimiter as content.
        var i = delimiterIndex + doubleQuoteCount;
        while (i < span.Length && span[i] == '"')
        {
            i++;
            delimiterIndex++;
        }

        stringLiteralLength = doubleQuoteCount + delimiterIndex + doubleQuoteCount;
        return isMultiline ?
            ScanStringLiteralResult.MultilineString :
            ScanStringLiteralResult.String;
    }

    private static int IndexOfInterpolationOrUnescapedQuote(ReadOnlySpan<char> text)
    {
        var offset = 0;
        while ((uint)offset < (uint)text.Length)
        {
            var relativeIndex = text[offset..].IndexOfAny(BackslashOrDoubleQuote);
            if (relativeIndex < 0)
            {
                return -1;
            }

            var index = offset + relativeIndex;
            if (text[index] == '"')
            {
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
                {
                    offset = index + 1;
                    continue;
                }
            }
            else if (next == '"')
            {
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
