// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

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

    /// <summary>A string literal without interpolation was found, on one line or several.</summary>
    String,

    /// <summary>An escaped string literal containing interpolation was found, on one line or several.</summary>
    Interpolation,
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
    /// <param name="content">
    /// The source of <paramref name="rawLiteral"/>, where <paramref name="koto"/> receives escape problems.
    /// </param>
    /// <returns>
    /// The decoded string value.
    /// </returns>
    /// <remarks>
    /// Invalid escape sequences are replaced with fallback characters. Each physical CRLF or CR in the text contributes one LF, as
    /// an LF does; escape results are not normalized (SPEC 2.9).
    /// </remarks>
    public static string GetStringLiteralValue(string rawLiteral, Koto? koto = default, SourceSpan content = default)
    {
        var span = rawLiteral.AsSpan();

        if (span.IsEmpty)
        {
            return string.Empty;
        }

        // The delimiters of an escaped string literal have already been removed.
        if (span[0] != '"')
        {
            var first = span.IndexOfAny('\\', '\r');

            if (first < 0)
            {
                return rawLiteral;
            }

            var decodedLength = first + GetDecodedLength(span.Slice(first), koto, content);

            return string.Create(
                decodedLength,
                new DecodeState(rawLiteral, first),
                static (destination, state) =>
                {
                    Decode(state.Source, state.First, destination);
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

        var raw = span.Slice(delimiterLength, contentLength);
        var firstCr = raw.IndexOf('\r');
        if (firstCr < 0)
        {
            return rawLiteral.Substring(delimiterLength, contentLength);
        }

        // A raw string has no escapes; only its physical line breaks are normalized.
        var normalizedLength = firstCr + GetNormalizedLength(raw.Slice(firstCr));
        return string.Create(
            normalizedLength,
            raw,
            static (destination, raw) =>
            {
                Normalize(raw, destination);
            });
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
    internal static bool TryReadCharacterEscape(ref ReadOnlySpan<char> span, Koto? koto, SourceSpan at, out uint scalar)
    {
        scalar = 0;
        if (span.IsEmpty)
        {
            koto?.AddDiagnostic(at, DiagnosticCode.UnsupportedEscape_Kd, '\\');
            return false;
        }

        var escape = span[0];
        span = span[1..];
        if (escape == 'u')
        {
            return TryReadUnicodeEscape(ref span, koto, at, out scalar);
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
            koto?.AddDiagnostic(at, DiagnosticCode.UnsupportedEscape_Kd, escape);
            return false;
        }

        scalar = (uint)value;
        return true;
    }

    // The length of escaped text decoded: an escape gives its scalar, and a CRLF or CR gives one LF (SPEC 2.9).
    private static int GetDecodedLength(ReadOnlySpan<char> span, Koto? koto, SourceSpan at)
    {
        var length = 0;
        while (!span.IsEmpty)
        {
            var index = span.IndexOfAny('\\', '\r');
            if (index < 0)
            {
                return length + span.Length;
            }

            length += index + 1;
            if (span[index] == '\r')
            {
                span = span[(index + LineBreakLength(span, index))..];
                continue;
            }

            span = span[(index + 1)..];
            var succeeded = TryReadCharacterEscape(ref span, koto, at, out var scalar);
            length += succeeded && scalar > 0xFFFF ? 1 : 0;
        }

        return length;
    }

    private static void Decode(string source, int first, Span<char> destination)
    {
        var span = source.AsSpan();
        var destinationIndex = first;
        span[..first].CopyTo(destination);
        span = span[first..];
        while (!span.IsEmpty)
        {
            var index = span.IndexOfAny('\\', '\r');
            if (index < 0)
            {
                span.CopyTo(destination[destinationIndex..]);
                destinationIndex += span.Length;
                break;
            }

            span[..index].CopyTo(destination[destinationIndex..]);
            destinationIndex += index;
            if (span[index] == '\r')
            {
                destination[destinationIndex++] = '\n';
                span = span[(index + LineBreakLength(span, index))..];
                continue;
            }

            span = span[(index + 1)..];
            if (!TryReadCharacterEscape(ref span, default, default, out var scalar))
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

    // The length of text whose CRLF and CR each give one LF.
    private static int GetNormalizedLength(ReadOnlySpan<char> span)
    {
        var length = 0;
        while (!span.IsEmpty)
        {
            var index = span.IndexOf('\r');
            if (index < 0)
            {
                return length + span.Length;
            }

            length += index + 1;
            span = span[(index + LineBreakLength(span, index))..];
        }

        return length;
    }

    private static void Normalize(ReadOnlySpan<char> span, Span<char> destination)
    {
        var destinationIndex = 0;
        while (!span.IsEmpty)
        {
            var index = span.IndexOf('\r');
            if (index < 0)
            {
                span.CopyTo(destination[destinationIndex..]);
                destinationIndex += span.Length;
                break;
            }

            span[..index].CopyTo(destination[destinationIndex..]);
            destinationIndex += index;
            destination[destinationIndex++] = '\n';
            span = span[(index + LineBreakLength(span, index))..];
        }

        Debug.Assert(destinationIndex == destination.Length);
    }

    // The length of the line break at a CR: two for CRLF, one for a lone CR.
    private static int LineBreakLength(ReadOnlySpan<char> span, int index)
        => index + 1 < span.Length && span[index + 1] == '\n' ? 2 : 1;

    private static bool TryReadUnicodeEscape(ref ReadOnlySpan<char> span, Koto? koto, SourceSpan at, out uint scalar)
    {
        scalar = 0;
        if (span.IsEmpty || span[0] != '(')
        {
            koto?.AddDiagnostic(at, DiagnosticCode.InvalidUnicodeEscape_Kd);
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
                    koto?.AddDiagnostic(at, DiagnosticCode.InvalidUnicodeEscape_Kd);

                    return false;
                }

                if (value > 0x10FFFF ||
                    value is >= 0xD800 and <= 0xDFFF)
                {
                    koto?.AddDiagnostic(at, DiagnosticCode.InvalidUnicodeScalar_Kd);

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

        koto?.AddDiagnostic(at, DiagnosticCode.InvalidUnicodeEscape_Kd);

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
        // Recover through the rest of the physical line, but never consume its line break (LF, CRLF, or CR):
        // the tokenizer must still see the next line's indentation and separate the following item.
        var linebreakIndex = span.IndexOfAny('\r', '\n');
        stringLiteralLength = quoteCount + (linebreakIndex >= 0 ? linebreakIndex : span.Length);
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
                return interpolated ? ScanStringLiteralResult.Interpolation : ScanStringLiteralResult.String;
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

        // Treat surplus quotes before the closing delimiter as content.
        var i = delimiterIndex + doubleQuoteCount;
        while (i < span.Length && span[i] == '"')
        {
            i++;
            delimiterIndex++;
        }

        stringLiteralLength = doubleQuoteCount + delimiterIndex + doubleQuoteCount;
        return ScanStringLiteralResult.String;
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
        public readonly int First;

        public DecodeState(string source, int first)
        {
            this.Source = source;
            this.First = first;
        }
    }
}
