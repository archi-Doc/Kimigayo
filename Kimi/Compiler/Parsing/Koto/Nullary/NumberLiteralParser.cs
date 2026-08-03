// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Lexing;

/// <summary>
/// Identifies the value type of a parsed numeric literal.
/// </summary>
public enum NumberLiteralKind : byte
{
    None,
    Int128,
    Double,
}

/// <summary>
/// Identifies the result of parsing a numeric literal.
/// </summary>
public enum NumberLiteralParseError : byte
{
    None,

    /// <summary>
    /// The input is empty.
    /// </summary>
    Empty,

    /// <summary>
    /// The input does not start with a decimal digit.
    /// </summary>
    MustStartWithDigit,

    /// <summary>
    /// The radix-prefixed integer contains an invalid digit,
    /// suffix, or identifier continuation.
    /// </summary>
    InvalidBasedInteger,

    /// <summary>
    /// The exponent does not start with a decimal digit.
    /// </summary>
    InvalidExponent,

    /// <summary>
    /// A type suffix or identifier continuation follows the literal.
    /// </summary>
    UnsupportedSuffixOrIdentifier,

    /// <summary>
    /// Characters remain after the numeric literal.
    /// </summary>
    TrailingCharacters,

    /// <summary>
    /// The literal has invalid syntax.
    /// </summary>
    InvalidSyntax,

    /// <summary>
    /// The integer value exceeds <see cref="Int128.MaxValue"/>.
    /// </summary>
    Int128Overflow,

    /// <summary>
    /// The floating-point value exceeds the finite range of <see cref="double"/>.
    /// </summary>
    DoubleOverflow,

    /// <summary>
    /// The floating-point value could not be converted.
    /// </summary>
    DoubleConversionFailed,
}

public static partial class TokenHelper
{
    private const int NumberLiteralStackallocThreshold = 256;

    /// <summary>
    /// Parses a numeric literal as either <see cref="Int128"/> or
    /// <see cref="double"/>.
    /// </summary>
    /// <param name="numberLiteral">The complete numeric literal.</param>
    /// <param name="kind">
    /// When successful, receives the parsed value type.
    /// </param>
    /// <param name="int128Value">
    /// When successful with <see cref="NumberLiteralKind.Int128"/>,
    /// receives the integer value.
    /// </param>
    /// <param name="doubleValue">
    /// When successful with <see cref="NumberLiteralKind.Double"/>,
    /// receives the floating-point value.
    /// </param>
    /// <returns>
    /// <see cref="NumberLiteralParseError.None"/> on success;
    /// otherwise, an error identifying the failure.
    /// </returns>
    public static NumberLiteralParseError ParseNumberLiteral(
        ReadOnlySpan<char> numberLiteral,
        out NumberLiteralKind kind,
        out Int128 int128Value,
        out double doubleValue)
    {
        kind = NumberLiteralKind.None;
        int128Value = default;
        doubleValue = default;

        if (numberLiteral.IsEmpty)
        {
            return NumberLiteralParseError.Empty;
        }

        if (!IsDigit(numberLiteral[0]))
        {
            return NumberLiteralParseError.MustStartWithDigit;
        }

        if (!ScanNumberLiteral(numberLiteral, out var scannedLength))
        {
            return ClassifyNumberLiteralSyntaxError(numberLiteral);
        }

        // ParseNumberLiteral expects the supplied span to contain exactly
        // one numeric literal.
        if (scannedLength != numberLiteral.Length)
        {
            return NumberLiteralParseError.TrailingCharacters;
        }

        if (TryGetRadixPrefix(
            numberLiteral,
            out var radix,
            out var digitsStart))
        {
            var error = ParseInt128Literal(
                numberLiteral,
                digitsStart,
                radix,
                out int128Value);

            if (error == NumberLiteralParseError.None)
            {
                kind = NumberLiteralKind.Int128;
            }

            return error;
        }

        if (ContainsDecimalPointOrExponent(numberLiteral))
        {
            var error = ParseDoubleLiteral(
                numberLiteral,
                out doubleValue);

            if (error == NumberLiteralParseError.None)
            {
                kind = NumberLiteralKind.Double;
            }

            return error;
        }

        {
            var error = ParseInt128Literal(
                numberLiteral,
                0,
                10,
                out int128Value);

            if (error == NumberLiteralParseError.None)
            {
                kind = NumberLiteralKind.Int128;
            }

            return error;
        }
    }

    private static NumberLiteralParseError ParseInt128Literal(
        ReadOnlySpan<char> text,
        int start,
        int radix,
        out Int128 value)
    {
        value = default;

        var current = UInt128.Zero;
        var maximum = (UInt128)Int128.MaxValue;
        var unsignedRadix = (uint)radix;

        // Calculate the overflow limits once rather than performing
        // division for every digit.
        var cutoff = maximum / unsignedRadix;
        var remainderLimit = (uint)(maximum % unsignedRadix);

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '_')
            {
                continue;
            }

            var digit = GetRadixDigitValueObs(c);
            if (current > cutoff ||
                (current == cutoff && digit > remainderLimit))
            {
                return NumberLiteralParseError.Int128Overflow;
            }

            current = (current * unsignedRadix) + digit;
        }

        value = (Int128)current;
        return NumberLiteralParseError.None;
    }

    private static NumberLiteralParseError ParseDoubleLiteral(
        ReadOnlySpan<char> text,
        out double value)
    {
        value = default;

        char[]? rentedArray = null;
        scoped Span<char> buffer;

        if (text.Length <= NumberLiteralStackallocThreshold)
        {
            buffer = stackalloc char[text.Length];
        }
        else
        {
            rentedArray = ArrayPool<char>.Shared.Rent(text.Length);
            buffer = rentedArray;
        }

        try
        {
            var written = 0;

            foreach (var c in text)
            {
                if (c != '_')
                {
                    buffer[written++] = c;
                }
            }

            if (!double.TryParse(
                buffer[..written],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value))
            {
                value = default;
                return NumberLiteralParseError.DoubleConversionFailed;
            }

            // Recent .NET implementations may return an infinity for a value
            // outside the finite Double range, so check it explicitly.
            if (!double.IsFinite(value))
            {
                value = default;
                return NumberLiteralParseError.DoubleOverflow;
            }

            return NumberLiteralParseError.None;
        }
        finally
        {
            if (rentedArray is not null)
            {
                ArrayPool<char>.Shared.Return(
                    rentedArray,
                    clearArray: false);
            }
        }
    }

    private static NumberLiteralParseError ClassifyNumberLiteralSyntaxError(
        ReadOnlySpan<char> text)
    {
        if (TryGetRadixPrefix(text, out _, out _))
        {
            return NumberLiteralParseError.InvalidBasedInteger;
        }

        // The first decimal digit has already been validated.
        var i = SkipDecimalDigitsAndSeparators(text, 1);

        if ((uint)i < (uint)text.Length &&
            text[i] == '.' &&
            (uint)(i + 1) < (uint)text.Length &&
            IsDigit(text[i + 1]))
        {
            i = SkipDecimalDigitsAndSeparators(text, i + 2);
        }

        if ((uint)i < (uint)text.Length &&
            (text[i] | 0x20) == 'e')
        {
            i++;

            if ((uint)i < (uint)text.Length &&
                (text[i] == '+' || text[i] == '-'))
            {
                i++;
            }

            if ((uint)i >= (uint)text.Length ||
                !IsDigit(text[i]))
            {
                return NumberLiteralParseError.InvalidExponent;
            }

            i = SkipDecimalDigitsAndSeparators(text, i + 1);
        }

        if ((uint)i < (uint)text.Length &&
            IsIdentifierContinue(text[i]))
        {
            return NumberLiteralParseError.UnsupportedSuffixOrIdentifier;
        }

        return NumberLiteralParseError.InvalidSyntax;
    }

    private static int SkipDecimalDigitsAndSeparators(
        ReadOnlySpan<char> text,
        int i)
    {
        while ((uint)i < (uint)text.Length)
        {
            var c = text[i];

            if (!IsDigit(c) && c != '_')
            {
                break;
            }

            i++;
        }

        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetRadixPrefix(
        ReadOnlySpan<char> text,
        out int radix,
        out int digitsStart)
    {
        if (text.Length >= 2 && text[0] == '0')
        {
            switch ((char)(text[1] | 0x20))
            {
                case 'b':
                    radix = 2;
                    digitsStart = 2;
                    return true;

                case 'o':
                    radix = 8;
                    digitsStart = 2;
                    return true;

                case 'x':
                    radix = 16;
                    digitsStart = 2;
                    return true;
            }
        }

        radix = 10;
        digitsStart = 0;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ContainsDecimalPointOrExponent(
        ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (c == '.' || (c | 0x20) == 'e')
            {
                return true;
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetRadixDigitValueObs(char c)
    {
        var digit = (uint)(c - '0');
        if (digit <= 9)
        {
            return digit;
        }

        return (uint)((c | 0x20) - 'a') + 10;
    }
}
