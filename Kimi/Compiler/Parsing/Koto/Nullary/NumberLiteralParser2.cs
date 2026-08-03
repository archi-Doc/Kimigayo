// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Globalization;

namespace Kimi.Compiler.Lexing;

/// <summary>
/// Indicates why <see cref="TokenHelper.TryParseNumberLiteral"/> failed to
/// produce a value, or <see cref="None"/> when parsing succeeded.
/// </summary>
public enum NumberLiteralParseResult
{
    /// <summary>Parsing succeeded.</summary>
    None = 0,

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
    /// <param name="value">When this method returns <see cref="NumberLiteralParseResult.None"/>, the parsed value.</param>
    /// <returns>
    /// <see cref="NumberLiteralParseResult.None"/> on success; otherwise, an error code describing
    /// why parsing failed.
    /// </returns>
    public static NumberLiteralParseResult TryParseNumberLiteral(ReadOnlySpan<char> numberLiteral, out Int128 i128Value, out double f64Value)
    {
        i128Value = default;
        f64Value = default;

        // Reject anything that is not, in its entirety, a single valid numeric literal.
        // This mirrors ScanNumberLiteral's own prefix dispatch below so the two stay in sync.
        if (!ScanNumberLiteral(numberLiteral, out var length) || length != numberLiteral.Length)
        {
            return NumberLiteralParseResult.InvalidFormat;
        }

        if (numberLiteral.Length >= 2 && numberLiteral[0] == '0')
        {
            switch ((char)(numberLiteral[1] | 0x20))
            {
                case 'b':
                    return ParseBasedInteger(numberLiteral[2..], 2, out i128Value);

                case 'o':
                    return ParseBasedInteger(numberLiteral[2..], 8, out i128Value);

                case 'x':
                    return ParseBasedInteger(numberLiteral[2..], 16, out i128Value);
            }
        }

        var isFloat = false;
        foreach (var c in numberLiteral)
        {
            if (c == '.' || (c | 0x20) == 'e')
            {
                isFloat = true;
                break;
            }
        }

        return isFloat
            ? ParseFloat(numberLiteral, out value)
            : ParseDecimalInteger(numberLiteral, out value);
    }

    /// <summary>
    /// Parses the digits (and '_' separators) following a radix prefix into an <see cref="Int128"/>.
    /// A digit sequence with no digits at all (e.g. "0x" or "0x____") represents zero.
    /// </summary>
    private static NumberLiteralParseResult ParseBasedInteger(ReadOnlySpan<char> digits, int radix, out Int128 value)
    {
        value = default;
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
                return NumberLiteralParseResult.IntegerOverflow;
            }

            accumulator = (accumulator * (uint)radix) + digitValue;
        }

        value = unchecked((Int128)accumulator);
        return NumberLiteralParseResult.None;
    }

    /// <summary>
    /// Parses a decimal digit sequence (and '_' separators), with no fractional part or
    /// exponent, into an <see cref="Int128"/>.
    /// </summary>
    private static NumberLiteralParseResult ParseDecimalInteger(ReadOnlySpan<char> text, out NumberLiteralValue value)
    {
        value = default;

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
                return NumberLiteralParseResult.IntegerOverflow;
            }

            accumulator = accumulator * 10 + digitValue;
        }

        value = NumberLiteralValue.FromInteger(accumulator);
        return NumberLiteralParseResult.None;
    }

    /// <summary>
    /// Parses a literal containing a fractional part and/or an exponent into a <see cref="double"/>.
    /// </summary>
    private static NumberLiteralParseResult ParseFloat(ReadOnlySpan<char> text, out NumberLiteralValue value)
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

        value = NumberLiteralValue.FromFloat(result);
        return NumberLiteralParseResult.None;
    }

    /// <summary>
    /// Returns the numeric value (0-15) of a hexadecimal/octal/binary digit. The caller
    /// must ensure <paramref name="c"/> is a valid digit for the literal's radix.
    /// </summary>
    private static int GetRadixDigitValue2(char c)
    {
        var value = (uint)(c - '0');
        if (value <= 9)
        {
            return (int)value;
        }

        return 10 + (int)((uint)((c | 0x20) - 'a'));
    }
}
