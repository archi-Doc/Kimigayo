// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;
using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Lexing;

/// <summary>
/// Indicates why <see cref="TokenHelper.TryParseNumberLiteral"/> failed to
/// produce a value, or <see cref="Success"/> when parsing succeeded.
/// </summary>
public enum NumberLiteralParseResult
{
    /// <summary>Parsing succeeded.</summary>
    Success = 0,

    /// <summary>
    /// The input is not a lexically valid numeric literal, or it contains
    /// trailing characters that are not part of the literal.
    /// </summary>
    InvalidFormat,

    /// <summary>
    /// The literal denotes an integer (decimal, or radix-prefixed) whose
    /// magnitude does not fit in <see cref="Int128"/>.
    /// </summary>
    IntegerOverflow,

    /// <summary>
    /// The literal denotes a floating-point value whose magnitude is too
    /// large to be represented as a finite <see cref="double"/> (it would
    /// parse to positive or negative infinity).
    /// </summary>
    FloatOverflow,
}

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
    /// <param name="value">When this method returns <see cref="NumberLiteralParseResult.Success"/>, the parsed value.</param>
    /// <returns>
    /// <see cref="NumberLiteralParseResult.Success"/> on success; otherwise, an error code describing
    /// why parsing failed.
    /// </returns>
    public static NumberLiteralParseResult TryParseNumberLiteral(ReadOnlySpan<char> numberLiteral, out Int128 value)
    {
        value = default;
        if (numberLiteral.Length >= 2 && numberLiteral[0] == '0')
        {
            switch ((char)(numberLiteral[1] | 0x20))
            {
                case 'b':
                    return ParseNumber(numberLiteral[2..], 2, out value);

                case 'o':
                    return ParseBasedInteger(numberLiteral[2..], 8, out value);

                case 'x':
                    return ParseBasedInteger(numberLiteral[2..], 16, out value);
            }
        }

        return numberLiteral.IndexOfAny('.', 'e', 'E') >= 0 ?
            ParseNumber(numberLiteral, 0, out value) : // Float
            ParseNumber(numberLiteral, 10, out value); // Decimal
    }

    private static NumberLiteralParseResult ParseNumber(ReadOnlySpan<char> text, int radix, out Int128 value)
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
                    return NumberLiteralParseResult.InvalidFormat;
                }

                value = v;
            }
            else
            {
                if (!Int128.TryParse(span, CultureInfo.InvariantCulture, out value))
                {
                    value = default;
                    return NumberLiteralParseResult.InvalidFormat;
                }
            }
        }
        else if (radix == 2)
        {
            if (span.Length <= 64)
            {
                if (!ulong.TryParse(span, NumberStyles.BinaryNumber, CultureInfo.InvariantCulture, out var v))
                {
                    value = default;
                    return NumberLiteralParseResult.InvalidFormat;
                }

                value = v;
            }
            else
            {
                if (!Int128.TryParse(span, NumberStyles.BinaryNumber, CultureInfo.InvariantCulture, out value))
                {
                    value = default;
                    return NumberLiteralParseResult.InvalidFormat;
                }
            }
        }
        else
        {// Float
            if (!double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            {
                value = default;
                return NumberLiteralParseResult.InvalidFormat;
            }

            if (double.IsInfinity(result))
            {
                value = default;
                return NumberLiteralParseResult.FloatOverflow;
            }

            value = BitConverter.DoubleToUInt64Bits(result);
        }

        return NumberLiteralParseResult.Success;
    }

    /// <summary>
    /// Parses the digits (and '_' separators) following a radix prefix into an <see cref="Int128"/>.
    /// A digit sequence with no digits at all (e.g. "0x" or "0x____") represents zero.
    /// </summary>
    private static NumberLiteralParseResult ParseBasedInteger(ReadOnlySpan<char> digits, int radix, out Int128 value)
    {
        UInt128 accumulator = 0;
        foreach (var c in digits)
        {
            if (c == '_')
            {
                continue;
            }

            var digitValue = (UInt128)GetRadixDigitValue(c);

            // accumulator * radix + digitValue must fit in 128 bits.
            if (accumulator > (UInt128.MaxValue - digitValue) / (uint)radix)
            {
                value = default;
                return NumberLiteralParseResult.IntegerOverflow;
            }

            accumulator = (accumulator * (uint)radix) + digitValue;
        }

        value = unchecked((Int128)accumulator);
        return NumberLiteralParseResult.Success;
    }

    /// <summary>
    /// Parses a decimal digit sequence (and '_' separators), with no fractional part or
    /// exponent, into an <see cref="Int128"/>.
    /// </summary>
    private static NumberLiteralParseResult ParseDecimalInteger(ReadOnlySpan<char> text, out Int128 value)
    {
        Int128 accumulator = 0;
        foreach (var c in text)
        {
            if (c == '_')
            {
                continue;
            }

            var digitValue = (Int128)(c - '0');

            // accumulator * 10 + digitValue must fit in Int128 (literals have no sign).
            if (accumulator > (Int128.MaxValue - digitValue) / 10)
            {
                value = default;
                return NumberLiteralParseResult.IntegerOverflow;
            }

            accumulator = (accumulator * 10) + digitValue;
        }

        value = accumulator;
        return NumberLiteralParseResult.Success;
    }

    private static NumberLiteralParseResult ParseDecimalInteger2(ReadOnlySpan<char> text, out Int128 value)
    {
        value = default;

        Span<char> buffer = text.Length <= 128 ? stackalloc char[128] : new char[text.Length];
        var writeIndex = 0;

        foreach (var c in text)
        {
            if (c != '_')
            {
                buffer[writeIndex++] = c;
            }
        }

        if (!Int128.TryParse(buffer[..writeIndex], CultureInfo.InvariantCulture, out value))
        {
            return NumberLiteralParseResult.InvalidFormat;
        }

        return NumberLiteralParseResult.Success;
    }

    /// <summary>
    /// Parses a literal containing a fractional part and/or an exponent into a <see cref="double"/>.
    /// </summary>
    private static NumberLiteralParseResult ParseFloat(ReadOnlySpan<char> text, out Int128 value)
    {
        value = default;

        // double.Parse does not understand '_' separators, so strip them into a scratch
        // buffer before handing the text to the runtime parser.
        Span<char> buffer = text.Length <= 128 ? stackalloc char[128] : new char[text.Length];
        var writeIndex = 0;

        foreach (var c in text)
        {
            if (c != '_')
            {
                buffer[writeIndex++] = c;
            }
        }

        // The text was already validated by ScanNumberLiteral, so this should not fail;
        // the check remains as a defensive guard against future grammar changes.
        if (!double.TryParse(buffer[..writeIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return NumberLiteralParseResult.InvalidFormat;
        }

        if (double.IsInfinity(result))
        {
            return NumberLiteralParseResult.FloatOverflow;
        }

        value = BitConverter.DoubleToUInt64Bits(result);
        return NumberLiteralParseResult.Success;
    }

    /// <summary>
    /// Returns the numeric value (0-15) of a hexadecimal/octal/binary digit.<br/>
    /// The caller must ensure <paramref name="c"/> is a valid digit for the literal's radix.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetRadixDigitValue(char c)
    {
        var alphabetic = c >> 6;
        return (c & 0x0F) + (alphabetic << 3) + alphabetic;

        /*var value = (uint)(c - '0');
        if (value <= 9)
        {
            return (int)value;
        }

        return 10 + (int)((uint)((c | 0x20) - 'a'));*/
    }
}
