// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;

namespace Kimi.Compiler.Lexing;

public static partial class TokenHelper
{
    /// <summary>
    /// Parses a numeric literal, as recognized by <see cref="ScanNumberLiteral"/>, into an
    /// <see cref="Int128"/> or a <see cref="double"/>.
    /// </summary>
    /// <param name="numberLiteral">
    /// The full text of the literal (e.g. a token's text). The entire span must form a single,
    /// lexically valid numeric literal; trailing characters are treated as a format error rather
    /// than being ignored.
    /// </param>
    /// <param name="value">When this method returns true, the parsed value.</param>
    /// <returns>
    /// Returns true if parsing succeeds.
    /// </returns>
    public static bool TryParseNumberLiteral(ReadOnlySpan<char> numberLiteral, out Int128 value)
    {
        value = default;
        if (numberLiteral.Length >= 2 && numberLiteral[0] == '0')
        {
            switch ((char)(numberLiteral[1] | 0x20))
            {
                case 'b':
                    return ParseNumber(numberLiteral[2..], 2, out value);

                case 'o':
                    return ParseOctalInteger(numberLiteral[2..], out value); // ParseBasedInteger(numberLiteral[2..], 8, out value);

                case 'x':
                    return ParseHexInteger(numberLiteral[2..], out value); // return ParseNumber(numberLiteral[2..], 16, out value);
            }
        }

        return numberLiteral.IndexOfAny('.', 'e', 'E') >= 0 ?
            ParseNumber(numberLiteral, 0, out value) : // Float
            ParseNumber(numberLiteral, 10, out value); // Decimal
    }

    private static bool ParseNumber(ReadOnlySpan<char> text, int radix, out Int128 value)
    {
        Span<char> buffer = text.Length <= 128 ? stackalloc char[128] : new char[text.Length];
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
            if (span.Length <= 19)
            {
                if (!long.TryParse(span, CultureInfo.InvariantCulture, out var v))
                {
                    value = default;
                    return false;
                }

                value = v;
            }
            else
            {
                if (!Int128.TryParse(span, CultureInfo.InvariantCulture, out value))
                {
                    value = default;
                    return false;
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
                    return false;
                }

                value = v;
            }
            else
            {
                if (!Int128.TryParse(span, NumberStyles.BinaryNumber, CultureInfo.InvariantCulture, out value))
                {
                    value = default;
                    return false;
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
                    return false;
                }

                value = v;
            }
            else
            {
                if (!Int128.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                {
                    value = default;
                    return false;
                }
            }
        }
        else
        {// Float
            if (!double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            {
                value = default;
                return false;
            }

            if (double.IsInfinity(result))
            {
                value = default;
                return false;
            }

            value = BitConverter.DoubleToUInt64Bits(result);
        }

        return true;
    }

    private static bool ParseHexInteger(ReadOnlySpan<char> digits, out Int128 value)
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
                return false;
            }

            var digitValue = (uint)((c & 0x0F) + ((c >> 6) * 9));
            accumulator = (accumulator << 4) | digitValue;
        }

        value = unchecked((Int128)accumulator);
        return true;
    }

    private static bool ParseOctalInteger(ReadOnlySpan<char> digits, out Int128 value)
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
                return false;
            }

            accumulator = (accumulator << 3) | (uint)(c - '0');
        }

        value = unchecked((Int128)accumulator);
        return true;
    }
}
