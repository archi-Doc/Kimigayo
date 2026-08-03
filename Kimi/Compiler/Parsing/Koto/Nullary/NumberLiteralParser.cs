// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

public enum NumberLiteralParseResult : byte
{
    /// <summary>
    /// The literal is invalid or its value exceeds 128 bits.
    /// </summary>
    Invalid,

    /// <summary>
    /// The result contains the parsed 128-bit integer bit pattern,
    /// interpreted as an <see cref="Int128"/>.
    /// </summary>
    I128,

    /// <summary>
    /// The lower 64 bits of the result contain the raw IEEE 754
    /// representation of a <see cref="double"/>.
    /// </summary>
    F64,
}

public static partial class NumberLiteralHelper
{
    /// <summary>
    /// Parses a numeric literal, as recognized by <see cref="TokenHelper.ScanNumberLiteral"/>, into an
    /// <see cref="Int128"/> or a <see cref="double"/>.
    /// </summary>
    /// <param name="numberLiteral">
    /// The full text of the literal (e.g. a token's text). The entire span must form a single,
    /// lexically valid numeric literal; trailing characters are treated as a format error rather
    /// than being ignored.
    /// </param>
    /// <param name="value">
    /// When the result is <see cref="NumberLiteralParseResult.I128"/>,
    /// contains the parsed integer value.<br/>
    /// When the result is <see cref="NumberLiteralParseResult.F64"/>,
    /// contains the raw IEEE 754 representation of the parsed
    /// <see cref="double"/> in its lower 64 bits.
    /// </param>
    /// <returns>
    /// The kind of parsed numeric literal, or
    /// <see cref="NumberLiteralParseResult.Invalid"/> if parsing fails.
    /// </returns>
    /// <remarks>
    /// The input must already have been validated by <see cref="TokenHelper.ScanNumberLiteral"/>.
    /// </remarks>
    public static NumberLiteralParseResult ParseNumberLiteral(ReadOnlySpan<char> numberLiteral, out Int128 value)
    {
        if (numberLiteral.Length >= 2 && numberLiteral[0] == '0')
        {
            switch ((char)(numberLiteral[1] | 0x20))
            {
                case 'b':
                    return ParseBinaryInteger(numberLiteral[2..], out value);

                case 'o':
                    return ParseOctalInteger(numberLiteral[2..], out value);

                case 'x':
                    return ParseHexInteger(numberLiteral[2..], out value);
            }
        }

        return numberLiteral.IndexOfAny('.', 'e', 'E') >= 0 ?
            ParseFloat(numberLiteral, out value) : // Float
            ParseDecimalInteger(numberLiteral, out value); // Decimal
    }

    private static NumberLiteralParseResult ParseFloat(ReadOnlySpan<char> text, out Int128 value)
    {
        Span<char> buffer = text.Length <= 128 ? stackalloc char[text.Length] : new char[text.Length];
        var writeIndex = 0;
        foreach (var c in text)
        {
            if (c != '_')
            {
                buffer[writeIndex++] = c;
            }
        }

        if (!double.TryParse(buffer[..writeIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            value = default;
            return NumberLiteralParseResult.Invalid;
        }

        if (double.IsInfinity(result))
        {
            value = default;
            return NumberLiteralParseResult.Invalid;
        }

        value = BitConverter.DoubleToUInt64Bits(result);
        return NumberLiteralParseResult.F64;
    }

    private static NumberLiteralParseResult ParseBinaryInteger(ReadOnlySpan<char> digits, out Int128 value)
    {
        if (digits.Length <= 64)
        {
            ulong accumulator = 0;
            foreach (var c in digits)
            {
                if (c == '_')
                {
                    continue;
                }

                var digit = (uint)(c - '0');
                accumulator = (accumulator << 1) | digit;
            }

            value = unchecked((Int128)accumulator);
            return NumberLiteralParseResult.I128;
        }
        else
        {
            UInt128 accumulator = 0;
            var maxBeforeShift = UInt128.MaxValue >> 1;
            foreach (var c in digits)
            {
                if (c == '_')
                {
                    continue;
                }

                var digit = (uint)(c - '0');

                if (accumulator > maxBeforeShift)
                {
                    value = default;
                    return NumberLiteralParseResult.Invalid;
                }

                accumulator = (accumulator << 1) | digit;
            }

            value = unchecked((Int128)accumulator);
            return NumberLiteralParseResult.I128;
        }
    }

    private static NumberLiteralParseResult ParseOctalInteger(ReadOnlySpan<char> digits, out Int128 value)
    {
        if (digits.Length <= 21)
        {
            ulong accumulator = 0;
            foreach (var c in digits)
            {
                if (c == '_')
                {
                    continue;
                }

                accumulator = (accumulator << 3) | (uint)(c - '0');
            }

            value = unchecked((Int128)accumulator);
            return NumberLiteralParseResult.I128;
        }
        else
        {
            UInt128 accumulator = 0;
            var maxBeforeShift = UInt128.MaxValue >> 3;
            foreach (var c in digits)
            {
                if (c == '_')
                {
                    continue;
                }

                if (accumulator > maxBeforeShift)
                {
                    value = default;
                    return NumberLiteralParseResult.Invalid;
                }

                accumulator = (accumulator << 3) | (uint)(c - '0');
            }

            value = unchecked((Int128)accumulator);
            return NumberLiteralParseResult.I128;
        }
    }

    private static NumberLiteralParseResult ParseDecimalInteger(ReadOnlySpan<char> digits, out Int128 value)
    {
        if (digits.Length <= 18)
        {
            ulong accumulator = 0;
            foreach (var c in digits)
            {
                if (c == '_')
                {
                    continue;
                }

                // The input has already been validated by ScanNumberLiteral().
                var digit = (uint)(c - '0');
                accumulator = (accumulator << 3) + (accumulator << 1) + digit;
            }

            value = unchecked((Int128)accumulator);
            return NumberLiteralParseResult.I128;
        }
        else
        {
            var maxBeforeMultiply = ((UInt128)0x1999_9999_9999_9999UL << 64) | 0x9999_9999_9999_9999UL;
            UInt128 accumulator = 0;
            foreach (var c in digits)
            {
                if (c == '_')
                {
                    continue;
                }

                // The input has already been validated by ScanNumberLiteral().
                var digit = (uint)(c - '0');

                if (accumulator > maxBeforeMultiply ||
                    (accumulator == maxBeforeMultiply && digit > 5))
                {
                    value = default;
                    return NumberLiteralParseResult.Invalid;
                }

                // accumulator * 10 + digit
                accumulator = (accumulator << 3) + (accumulator << 1) + digit;
            }

            value = unchecked((Int128)accumulator);
            return NumberLiteralParseResult.I128;
        }
    }

    private static NumberLiteralParseResult ParseHexInteger(ReadOnlySpan<char> digits, out Int128 value)
    {
        if (digits.Length <= 16)
        {
            ulong accumulator = 0;
            foreach (var c in digits)
            {
                if (c == '_')
                {
                    continue;
                }

                var digitValue = (uint)((c & 0x0F) + ((c >> 6) * 9));
                accumulator = (accumulator << 4) | digitValue;
            }

            value = unchecked((Int128)accumulator);
            return NumberLiteralParseResult.I128;
        }
        else
        {
            UInt128 accumulator = 0;
            var maxBeforeShift = UInt128.MaxValue >> 4;
            foreach (var c in digits)
            {
                if (c == '_')
                {
                    continue;
                }

                if (accumulator > maxBeforeShift)
                {
                    value = default;
                    return NumberLiteralParseResult.Invalid;
                }

                var digitValue = (uint)((c & 0x0F) + ((c >> 6) * 9));
                accumulator = (accumulator << 4) | digitValue;
            }

            value = unchecked((Int128)accumulator);
            return NumberLiteralParseResult.I128;
        }
    }
}
