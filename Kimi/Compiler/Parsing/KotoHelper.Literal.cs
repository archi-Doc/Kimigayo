// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public static partial class KotoHelper
{
    public static string ParseLiteral(string rawLiteral, Koto? koto = default)
    {// WithEscape, """WithoutEscape"""
        var span = rawLiteral.AsSpan();

        if (span.IsEmpty)
        {
            return string.Empty;
        }

        if (span[0] != '"')
        {// Text + Escape
            var firstBackslash = rawLiteral.IndexOf('\\');
            if (firstBackslash < 0)
            {// Fast path (no escape)
                return rawLiteral;
            }

            var decodedLength = firstBackslash +
                GetUnescapedLength(rawLiteral.AsSpan(firstBackslash, rawLiteral.Length - firstBackslash), koto);

            return string.Create(decodedLength, new DecodeState(rawLiteral, firstBackslash), static (destination, state) => Decode(state.Source, state.FirstBackslash, destination));
        }

        var leadingQuoteCount = 0;
        while (leadingQuoteCount < span.Length && span[leadingQuoteCount] == '"')
        {
            leadingQuoteCount++;
        }

        // """RawText"""
        return rawLiteral.Substring(leadingQuoteCount, rawLiteral.Length - leadingQuoteCount - leadingQuoteCount).ToString();
    }

    private static int GetUnescapedLength(ReadOnlySpan<char> span, Koto? koto)
    {
        var length = 0;
        while (!span.IsEmpty)
        {
            var backslashIndex = span.IndexOf('\\');
            if (backslashIndex < 0)
            {
                return length;
            }

            length += backslashIndex;
            span = span.Slice(backslashIndex + 1);
            if (span.IsEmpty)
            {
                return length;
            }

            switch (span[0])
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
                    var scalar = ReadUnicodeEscape(ref span, koto);
                    length += scalar <= 0xFFFF ? 1 : 2;
                    break;

                default:
                    koto?.AddDiagnostic(Hashed.Kimi.UnsupportedEscape, span[0]);
                    break;
            }
        }

        return length;
    }

    private static void Decode(string source, int firstBackslash, Span<char> destination)
    {
        var span = source.AsSpan(1, source.Length - 2);
        span.Slice(0, firstBackslash - 1).CopyTo(destination);

        var sourceLength = span.Length;
        var sourceIndex = firstBackslash - 1;
        var destinationIndex = firstBackslash - 1;

        while (sourceIndex < sourceLength)
        {
            var c = source[sourceIndex++];

            if (c != '\\')
            {
                destination[destinationIndex++] = c;
                continue;
            }

            char escapeKind = source[sourceIndex++];

            switch (escapeKind)
            {
                case '0':
                    destination[destinationIndex++] = '\0';
                    break;

                case '\\':
                    destination[destinationIndex++] = '\\';
                    break;

                case 't':
                    destination[destinationIndex++] = '\t';
                    break;

                case 'n':
                    destination[destinationIndex++] = '\n';
                    break;

                case 'r':
                    destination[destinationIndex++] = '\r';
                    break;

                case '"':
                    destination[destinationIndex++] = '"';
                    break;

                case '\'':
                    destination[destinationIndex++] = '\'';
                    break;

                case 'u':
                    {
                        var scalar = ReadUnicodeEscape(ref span, default);
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
                    }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadUnicodeEscape(ref ReadOnlySpan<char> span, Koto? koto)
    {
        if (span.IsEmpty || span[0] != '(')
        {
            koto?.AddDiagnostic(Hashed.Kimi.InvalidUnicodeEscape);
            return 0;
        }

        uint value = 0;
        var digitCount = 0;
        while (!span.IsEmpty)
        {
            var c = span[0];
            span = span.Slice(1);

            if (c == ')')
            {
                if (digitCount == 0)
                {
                    koto?.AddDiagnostic(Hashed.Kimi.InvalidUnicodeEscape);
                    value = 0;
                }

                if (value > 0x10FFFF || value is >= 0xD800 and <= 0xDFFF)
                {
                    koto?.AddDiagnostic(Hashed.Kimi.InvalidUnicodeScalar);
                    value = 0;
                }

                return value;
            }

            var digit = GetHexValue(c);
            if (digit < 0 || digitCount == 6)
            {
                koto?.AddDiagnostic(Hashed.Kimi.InvalidUnicodeEscape);
            }

            value = (value << 4) | (uint)digit;
            digitCount++;
        }

        koto?.AddDiagnostic(Hashed.Kimi.InvalidUnicodeEscape);
        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetHexValue(char c)
    {
        uint value = c;
        if (value - '0' <= 9)
        {
            return (int)(value - '0');
        }

        value = (value | 0x20) - 'a';
        return value <= 5 ? (int)value + 10 : -1;
    }
}
