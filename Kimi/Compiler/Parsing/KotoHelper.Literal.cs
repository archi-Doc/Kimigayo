// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public static partial class KotoHelper
{
    private const char UnicodeFallbackChar = 'k';

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
                return length + span.Length;
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
                    span = span.Slice(1);
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
        var span = source.AsSpan();
        var dest = destination;
        span.Slice(0, firstBackslash).CopyTo(dest);
        span = span.Slice(firstBackslash);
        dest = dest.Slice(firstBackslash);

        var destinationIndex = firstBackslash;
        while (!span.IsEmpty)
        {
            var backslashIndex = span.IndexOf('\\');
            if (backslashIndex < 0)
            {
                span.CopyTo(dest);
                dest = dest.Slice(span.Length);
                break;
            }
            else if (backslashIndex > 0)
            {
                span.Slice(0, backslashIndex).CopyTo(dest);
                dest = dest.Slice(backslashIndex);
            }

            span = span.Slice(backslashIndex + 1);
            if (span.IsEmpty)
            {
                break;
            }

            char escape = span[0];
            if (escape != 'u')
            {
                dest[0] = escape switch
                {
                    '0' => '\0',
                    '\\' => '\\',
                    'e' => '\u001b',
                    't' => '\t',
                    'n' => '\n',
                    '"' => '"',
                    '\'' => '\'',
                    _ => UnicodeFallbackChar,
                };

                span = span.Slice(1);
                dest = dest.Slice(1);
                continue;
            }
            else
            {// u(1234)
                span = span.Slice(1);
                var scalar = ReadUnicodeEscape(ref span, default);
                if (scalar == 0)
                {
                    dest[0] = UnicodeFallbackChar;
                    dest = dest.Slice(1);
                }
                else if (scalar <= 0xFFFF)
                {
                    dest[0] = (char)scalar;
                    dest = dest.Slice(1);
                }
                else
                {
                    scalar -= 0x10000;
                    dest[0] = (char)(0xD800 + (scalar >> 10));
                    dest[1] = (char)(0xDC00 + (scalar & 0x3FF));
                    dest = dest.Slice(2);
                }
            }
        }

        Debug.Assert(dest.Length == 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReadUnicodeEscape(ref ReadOnlySpan<char> span, Koto? koto)
    {
        if (span.IsEmpty || span[0] != '(')
        {
            koto?.AddDiagnostic(Hashed.Kimi.InvalidUnicodeEscape);
            return 0;
        }

        span = span.Slice(1);

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
                    koto = default;
                    value = 0;
                }

                if (value > 0x10FFFF || value is >= 0xD800 and <= 0xDFFF)
                {
                    koto?.AddDiagnostic(Hashed.Kimi.InvalidUnicodeScalar);
                    koto = default;
                    value = 0;
                }

                return value;
            }

            var digit = GetHexValue(c);
            if (digit < 0 || digitCount == 6)
            {
                koto?.AddDiagnostic(Hashed.Kimi.InvalidUnicodeEscape);
                koto = default;
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
