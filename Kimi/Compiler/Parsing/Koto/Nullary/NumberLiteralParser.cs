// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;

namespace Kimi.Compiler.Lexing;

public static partial class TokenHelper
{
    public enum ParseNumberLiteralResult
    {
        I128,
        F64,
        Invalid,
    }

    /// <summary>
    /// Parses a numeric literal, as recognized by <see cref="ScanNumberLiteral"/>, into an
    /// <see cref="Int128"/> or a <see cref="double"/>.
    /// </summary>
    /// <param name="numberLiteral">
    /// The full text of the literal (e.g. a token's text). The entire span must form a single,
    /// lexically valid numeric literal; trailing characters are treated as a format error rather
    /// than being ignored.
    /// </param>
    /// <param name="value">When this method returns true, the parsed value.<br/>
    /// For floating-point literals, <paramref name="value"/> contains the raw
    /// IEEE 754 bit representation of the parsed <see cref="double"/> in its
    /// lower 64 bits.</param>
    /// <returns>
    /// Returns true if parsing succeeds.
    /// </returns>
    /// <remarks>
    /// The input must already have been validated by <see cref="ScanNumberLiteral"/>.
    /// </remarks>
    public static ParseNumberLiteralResult TryParseNumberLiteral(ReadOnlySpan<char> numberLiteral, out Int128 value)
    {
        value = default;
        if (numberLiteral.Length >= 2 && numberLiteral[0] == '0')
        {
            switch ((char)(numberLiteral[1] | 0x20))
            {
                case 'b':
                    return ParseBinaryInteger(numberLiteral[2..], out value); // ParseNumber(numberLiteral[2..], 2, out value);

                case 'o':
                    return ParseOctalInteger(numberLiteral[2..], out value); // ParseBasedInteger(numberLiteral[2..], 8, out value);

                case 'x':
                    return ParseHexInteger(numberLiteral[2..], out value); // return ParseNumber(numberLiteral[2..], 16, out value);
            }
        }

        return numberLiteral.IndexOfAny('.', 'e', 'E') >= 0 ?
            ParseNumber(numberLiteral, 0, out value) : // Float
            ParseDecimalInteger(numberLiteral, out value);// ParseNumber(numberLiteral, 10, out value); // Decimal
    }

    private static ParseNumberLiteralResult ParseNumber(ReadOnlySpan<char> text, int radix, out Int128 value)
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

        var span = buffer[..writeIndex];
        if (radix == 10)
        {// Decimal
            if (span.Length <= 18)
            {
                if (!long.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out var v))
                {
                    value = default;
                    return ParseNumberLiteralResult.Invalid;
                }

                value = v;
            }
            else
            {
                if (!Int128.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out value))
                {
                    value = default;
                    return ParseNumberLiteralResult.Invalid;
                }
            }
        }
        else if (radix == 2)
        {// Binary
            if (span.Length <= 64)
            {
                if (!ulong.TryParse(span, NumberStyles.BinaryNumber, CultureInfo.InvariantCulture, out var v))
                {
                    value = default;
                    return ParseNumberLiteralResult.Invalid;
                }

                value = v;
            }
            else
            {
                if (!Int128.TryParse(span, NumberStyles.BinaryNumber, CultureInfo.InvariantCulture, out value))
                {
                    value = default;
                    return ParseNumberLiteralResult.Invalid;
                }
            }
        }
        else if (radix == 16)
        {// Hex
            if (span.Length <= 16)
            {
                if (!ulong.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                {
                    value = default;
                    return ParseNumberLiteralResult.Invalid;
                }

                value = v;
            }
            else
            {
                if (!Int128.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                {
                    value = default;
                    return ParseNumberLiteralResult.Invalid;
                }
            }
        }
        else
        {// Float
            if (!double.TryParse(span, NumberStyles.Float | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var result))
            {
                value = default;
                return ParseNumberLiteralResult.Invalid;
            }

            if (double.IsInfinity(result))
            {
                value = default;
                return ParseNumberLiteralResult.Invalid;
            }

            value = BitConverter.DoubleToUInt64Bits(result);
            return ParseNumberLiteralResult.F64;
        }

        return ParseNumberLiteralResult.I128;
    }

    private static ParseNumberLiteralResult ParseHexInteger(ReadOnlySpan<char> digits, out Int128 value)
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
                return ParseNumberLiteralResult.Invalid;
            }

            var digitValue = (uint)((c & 0x0F) + ((c >> 6) * 9));
            accumulator = (accumulator << 4) | digitValue;
        }

        value = unchecked((Int128)accumulator);
        return ParseNumberLiteralResult.I128;
    }

    private static ParseNumberLiteralResult ParseDecimalInteger(ReadOnlySpan<char> digits, out Int128 value)
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
            return ParseNumberLiteralResult.I128;
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
                    return ParseNumberLiteralResult.Invalid;
                }

                // accumulator * 10 + digit
                accumulator = (accumulator << 3) + (accumulator << 1) + digit;
            }

            value = unchecked((Int128)accumulator);
            return ParseNumberLiteralResult.I128;
        }
    }

    private static ParseNumberLiteralResult ParseOctalInteger(ReadOnlySpan<char> digits, out Int128 value)
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
                return ParseNumberLiteralResult.Invalid;
            }

            accumulator = (accumulator << 3) | (uint)(c - '0');
        }

        value = unchecked((Int128)accumulator);
        return ParseNumberLiteralResult.I128;
    }

    private static ParseNumberLiteralResult ParseBinaryInteger(ReadOnlySpan<char> digits, out Int128 value)
    {
        UInt128 accumulator = 0;
        var maxBeforeShift = UInt128.MaxValue >> 1;

        foreach (var c in digits)
        {
            if (c == '_')
            {
                continue;
            }

            // Remove this check in a scanner-validated hot path.
            var digit = (uint)(c - '0');
            if (digit > 1)
            {
                value = default;
                return ParseNumberLiteralResult.Invalid;
            }

            if (accumulator > maxBeforeShift)
            {
                value = default;
                return ParseNumberLiteralResult.Invalid;
            }

            accumulator = (accumulator << 1) | digit;
        }

        value = unchecked((Int128)accumulator);
        return ParseNumberLiteralResult.I128;
    }
}
