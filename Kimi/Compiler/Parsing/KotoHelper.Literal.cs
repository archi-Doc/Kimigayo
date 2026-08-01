// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public static partial class KotoHelper
{
    public static string ParseLiteral(string rawLiteral, Koto? koto = default)
    {
        var span = rawLiteral.AsSpan();
        var leadingQuoteCount = 0;
        while (leadingQuoteCount < span.Length && span[leadingQuoteCount] == '"')
        {
            leadingQuoteCount++;
        }

        if (leadingQuoteCount == 1)
        {// "Text" + Escape
            return ParseRegularLiteral(rawLiteral, koto);
        }
        else if (leadingQuoteCount >= 3)
        {// """RawText"""
            return ParseRawLiteral(rawLiteral, leadingQuoteCount, koto);
        }

        return string.Empty;
    }

    private static string ParseRegularLiteral(string rawLiteral, Koto? koto)
    {
        var firstBackslash = rawLiteral.IndexOf('\\');
        if (firstBackslash < 0)
        {// Fast path (no escape)
            return rawLiteral.Substring(1, rawLiteral.Length - 2);
        }

        var decodedLength = rawLiteral.Length - 2 +
            GetAdditionalLength(rawLiteral.AsSpan(firstBackslash, rawLiteral.Length - firstBackslash - 1), koto);

        return string.Create(decodedLength, new DecodeState(rawLiteral, firstBackslash), static (destination, state) => Decode(state.Source, state.FirstBackslash, destination));
    }

    private static string ParseRawLiteral(ReadOnlySpan<char> rawLiteral, int delimiterLength, Koto? koto)
    {
        var sourceLength = rawLiteral.Length;
        if (sourceLength < delimiterLength * 2)
        {
            koto?.AddDiagnostic(Hashed.Kimi.IncompleteEscape);
            return string.Empty;
        }

        int i;
        for (i = 1; i <= delimiterLength; i++)
        {
            if (rawLiteral[sourceLength - i] != '"')
            {
                break;
            }
        }

        return rawLiteral.Slice(delimiterLength, sourceLength - delimiterLength - i).ToString();
    }

    private static int GetAdditionalLength(ReadOnlySpan<char> span, Koto? koto)
    {
        var additionalLength = 0;
        while (!span.IsEmpty)
        {
            var c = span[0];
            if (c != '\\')
            {
                additionalLength++;
                continue;
            }

            span = span.Slice(1);
            if (span.IsEmpty)
            {

                return additionalLength;
            }

            c = span[0];
            switch (c)
            {
                case '0':
                case '\\':
                case 't':
                case 'n':
                case 'r':
                case '"':
                case '\'':
                    additionalLength++;
                    break;

                case 'u':
                    {
                        var scalar = ReadUnicodeEscape(source, ref index);
                        additionalLength += scalar <= 0xFFFF ? 1 : 2;
                        break;
                    }

                default:
                    koto?.AddDiagnostic(Hashed.Kimi.UnsupportedEscape, c);
                    break;
            }
        }

        return additionalLength;
    }

    private static void Decode(string source, int firstBackslash, Span<char> destination)
    {
        source.AsSpan(0, firstBackslash).CopyTo(destination);

        var sourceLength = source.Length;
        var sourceIndex = firstBackslash;
        var destinationIndex = firstBackslash;

        while (sourceIndex < sourceLength)
        {
            char c = source[sourceIndex++];

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
                        var scalar = ReadUnicodeEscape(source, ref sourceIndex);
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
        if (span.IsEmpty || span[0] != '{')
        {
            koto?.AddDiagnostic(Hashed.Kimi.InvalidUnicodeEscape);
            return 0;
        }

        uint value = 0;
        int digitCount = 0;

        while (!span.IsEmpty)
        {
            char c = span[0];
            span = span.Slice(1);

            if (c == '}')
            {
                if (digitCount == 0)
                {
                    koto?.AddDiagnostic(Hashed.Kimi.InvalidUnicodeEscape);

                    value = 0;
                }

                if (value > 0x10FFFF ||
                    value is >= 0xD800 and <= 0xDFFF)
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
