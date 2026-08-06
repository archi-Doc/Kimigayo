// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public static partial class KotoHelper
{
    private const char UnicodeFallbackChar = 'k';

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

    /// <summary>
    /// Decodes an escaped string literal or removes the delimiters from
    /// a raw string literal.
    /// </summary>
    public static string ParseLiteral(string rawLiteral, Koto? koto = default)
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

            var decodedLength =
                firstBackslash +
                GetDecodedLength(span.Slice(firstBackslash), koto);

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

        while (delimiterLength < span.Length &&
               span[delimiterLength] == '"')
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

        if (contentLength == 0)
        {
            return string.Empty;
        }

        return rawLiteral.Substring(
            delimiterLength,
            contentLength);
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
                koto?.AddDiagnostic(
                    Hashed.Kimi.UnsupportedEscape,
                    '\\');

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
                    koto?.AddDiagnostic(Hashed.Kimi.UnsupportedEscape, escape);

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

        span.Slice(0, firstBackslash)
            .CopyTo(destination);

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
                span.Slice(0, backslashIndex)
                    .CopyTo(destination.Slice(destinationIndex));

                span = span.Slice(backslashIndex);
                destinationIndex += backslashIndex;
            }

            // Skip '\'.
            span = span.Slice(1);

            if (span.IsEmpty)
            {
                destination[destinationIndex++] =
                    UnicodeFallbackChar;

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
                        destination[destinationIndex++] =
                            UnicodeFallbackChar;

                        break;
                    }

                    if (scalar <= 0xFFFF)
                    {
                        destination[destinationIndex++] =
                            (char)scalar;
                    }
                    else
                    {
                        scalar -= 0x10000;

                        destination[destinationIndex++] =
                            (char)(0xD800 + (scalar >> 10));

                        destination[destinationIndex++] =
                            (char)(0xDC00 + (scalar & 0x3FF));
                    }

                    break;

                default:
                    destination[destinationIndex++] =
                        UnicodeFallbackChar;

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
            koto?.AddDiagnostic(
                Hashed.Kimi.InvalidUnicodeEscape);

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
                    koto?.AddDiagnostic(Hashed.Kimi.InvalidUnicodeEscape);

                    return false;
                }

                if (value > 0x10FFFF ||
                    value is >= 0xD800 and <= 0xDFFF)
                {
                    koto?.AddDiagnostic(Hashed.Kimi.InvalidUnicodeScalar);

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

        koto?.AddDiagnostic(Hashed.Kimi.InvalidUnicodeEscape);

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
}
