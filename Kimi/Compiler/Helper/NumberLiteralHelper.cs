// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Helper;

/// <summary>
/// Identifies the result of parsing a numeric literal.
/// </summary>
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

/// <summary>
/// Provides methods for scanning and parsing numeric literals.
/// </summary>
public static partial class NumberLiteralHelper
{
    private static readonly SearchValues<char> FloatChars = SearchValues.Create(".eE");

    /// <summary>
    /// Determines whether a value fits in a signed 64-bit integer.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> if the value fits; otherwise, <see langword="false"/>.</returns>
    public static bool IsInt64(Int128 value)
        => value >= long.MinValue && value <= long.MaxValue;

    /// <summary>
    /// Scans a numeric literal at the start of <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="length">
    /// When this method returns, contains the number of characters consumed by
    /// the valid or malformed numeric literal.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if a lexically valid numeric literal was found;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public static bool ScanNumberLiteral(ReadOnlySpan<char> text, out int length)
    {
        var textLength = text.Length;

        if (textLength == 0)
        {
            length = 0;
            return false;
        }

        var first = text[0];

        if ((uint)(first - '0') > 9u)
        {
            length = 0;
            return false;
        }

        if (first == '0' && textLength >= 2)
        {
            switch ((char)(text[1] | 0x20))
            {
                case 'b':
                    return FinishNumberLiteral(text, ScanBinaryDigitsAndSeparators(text, 2), out length);

                case 'o':
                    return FinishNumberLiteral(text, ScanOctalDigitsAndSeparators(text, 2), out length);

                case 'x':
                    return FinishNumberLiteral(text, ScanHexadecimalDigitsAndSeparators(text, 2), out length);
            }
        }

        // The first decimal digit was validated above.
        var i = ScanDecimalDigitsAndSeparators(text, 1);

        // A fractional part requires at least one digit after the decimal point.
        if ((uint)i < (uint)textLength && text[i] == '.')
        {
            var fractionStart = i + 1;

            if ((uint)fractionStart < (uint)textLength &&
                (uint)(text[fractionStart] - '0') <= 9u)
            {
                i = ScanDecimalDigitsAndSeparators(text, fractionStart + 1);
            }
        }

        // Decimal exponent.
        if ((uint)i < (uint)textLength && (text[i] | 0x20) == 'e')
        {
            i++;

            if ((uint)i < (uint)textLength)
            {
                var sign = text[i];
                if (sign == '+' || sign == '-')
                {
                    i++;
                }
            }

            // A digit must immediately follow 'e', 'E', '+' or '-'.
            if ((uint)i >= (uint)textLength || (uint)(text[i] - '0') > 9u)
            {
                length = ExtendWithIdentifierContinue(text, i);
                return false;
            }

            i = ScanDecimalDigitsAndSeparators(text, i + 1);
        }

        return FinishNumberLiteral(text, i, out length);
    }

    /// <summary>
    /// Parses a validated numeric literal.
    /// </summary>
    /// <param name="numberLiteral">
    /// The complete literal text recognized by <see cref="ScanNumberLiteral"/>.
    /// </param>
    /// <param name="value">
    /// When the result is <see cref="NumberLiteralParseResult.I128"/>,
    /// contains the parsed integer value.<br/>
    /// When the result is <see cref="NumberLiteralParseResult.F64"/>,
    /// contains the raw IEEE 754 representation of the parsed
    /// <see cref="double"/> in its lower 64 bits.
    /// </param>
    /// <returns>
    /// The parsed literal kind, or <see cref="NumberLiteralParseResult.Invalid"/> on failure.
    /// </returns>
    public static NumberLiteralParseResult ParseNumberLiteral(ReadOnlySpan<char> numberLiteral, out Int128 value)
    {
        // Short decimal integers need neither a floating-point marker scan nor 128-bit arithmetic.
        if ((uint)(numberLiteral.Length - 1) < 9)
        {
            uint accumulator = 0;
            foreach (var c in numberLiteral)
            {
                var digit = (uint)(c - '0');
                if (digit > 9)
                {
                    goto GeneralLiteral;
                }

                accumulator = (accumulator * 10) + digit;
            }

            value = accumulator;
            return NumberLiteralParseResult.I128;
        }

GeneralLiteral:
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

        return numberLiteral.IndexOfAny(FloatChars) >= 0 ?
            ParseFloat(numberLiteral, out value) :
            ParseDecimalInteger(numberLiteral, out value);
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

                // Scanning has already validated each digit.
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

                // Scanning has already validated each digit.
                var digit = (uint)(c - '0');

                if (accumulator > maxBeforeMultiply ||
                    (accumulator == maxBeforeMultiply && digit > 5))
                {
                    value = default;
                    return NumberLiteralParseResult.Invalid;
                }

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

    // Include identifier continuations in a malformed token.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool FinishNumberLiteral(ReadOnlySpan<char> text, int i, out int length)
    {
        if ((uint)i < (uint)text.Length &&
            IsIdentifierContinue(text[i]))
        {
            length = ExtendWithIdentifierContinue(text, i);
            return false;
        }

        length = i;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ScanBinaryDigitsAndSeparators(ReadOnlySpan<char> text, int i)
    {
        var textLength = text.Length;
        while ((uint)i < (uint)textLength)
        {
            var c = text[i];

            if ((uint)(c - '0') > 1u && c != '_')
            {
                break;
            }

            i++;
        }

        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ScanOctalDigitsAndSeparators(ReadOnlySpan<char> text, int i)
    {
        var textLength = text.Length;
        while ((uint)i < (uint)textLength)
        {
            var c = text[i];

            if ((uint)(c - '0') > 7u && c != '_')
            {
                break;
            }

            i++;
        }

        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ScanDecimalDigitsAndSeparators(ReadOnlySpan<char> text, int i)
    {
        var textLength = text.Length;
        while ((uint)i < (uint)textLength)
        {
            var c = text[i];

            if ((uint)(c - '0') > 9u && c != '_')
            {
                break;
            }

            i++;
        }

        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ScanHexadecimalDigitsAndSeparators(ReadOnlySpan<char> text, int i)
    {
        var textLength = text.Length;
        while ((uint)i < (uint)textLength)
        {
            var c = text[i];

            // Fold ASCII uppercase letters before checking the hexadecimal range.
            if ((uint)(c - '0') > 9u &&
                (uint)((c | 0x20) - 'a') > 5u &&
                c != '_')
            {
                break;
            }

            i++;
        }

        return i;
    }

    // Consume the remainder of a malformed numeric token.
    private static int ExtendWithIdentifierContinue(ReadOnlySpan<char> text, int i)
    {
        var textLength = text.Length;
        while ((uint)i < (uint)textLength)
        {
            var c = text[i];

            if (!IsIdentifierContinue(c))
            {
                break;
            }

            i++;
        }

        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierContinue(char c)
        => (uint)(c - '0') <= 9u ||
            c == '_' ||
            (uint)((c | 0x20) - 'a') <= 25u ||
            c >= '\u0080';
}
