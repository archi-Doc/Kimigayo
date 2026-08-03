// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Lexing;

public static partial class TokenHelper
{
    /// <summary>
    /// Scans a numeric literal at the start of <paramref name="text"/>.<br/>
    /// Returns <see langword="true"/> when a lexically valid literal was found;
    /// <paramref name="length"/> is its length.<br/>
    /// Returns <see langword="false"/> with <paramref name="length"/> == 0 when
    /// the text does not start with a digit.<br/>
    /// Returns <see langword="false"/> with <paramref name="length"/> &gt; 0 when
    /// the text starts with a digit but does not form a valid literal.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="length">
    /// When this method returns, contains the number of characters consumed by
    /// the literal or malformed literal.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if a lexically valid numeric literal was found;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public static bool ScanNumberLiteral(ReadOnlySpan<char> text, out int length)
    {
        length = 0;
        if (text.IsEmpty || !IsDigit(text[0]))
        {
            return false;
        }

        if (text.Length >= 2 && text[0] == '0')
        {
            var prefix = (char)(text[1] | 0x20);

            if (prefix == 'b')
            {
                return ScanBasedInteger(text, 2, 2, out length);
            }

            if (prefix == 'o')
            {
                return ScanBasedInteger(text, 2, 8, out length);
            }

            if (prefix == 'x')
            {
                return ScanBasedInteger(text, 2, 16, out length);
            }
        }

        // The literal starts with a digit, so separators become allowed
        // after the first digit has been consumed.
        var i = ScanDigitsAndSeparators(text, 0, 10, precededByDigitOrPrefix: false, out _);

        // A fractional part must contain at least one digit.
        // Separators cannot appear immediately after the decimal point.
        if ((uint)i < (uint)text.Length && text[i] == '.')
        {
            var fractionEnd = ScanDigitsAndSeparators(text, i + 1, 10, precededByDigitOrPrefix: false, out var hasFractionDigit);

            if (hasFractionDigit)
            {
                i = fractionEnd;
            }
        }

        // Separators cannot appear immediately after 'e', 'E',
        // or the optional exponent sign.
        if ((uint)i < (uint)text.Length &&
            (text[i] | 0x20) == 'e')
        {
            i++;

            if ((uint)i < (uint)text.Length &&
                (text[i] == '+' || text[i] == '-'))
            {
                i++;
            }

            i = ScanDigitsAndSeparators(text, i, 10, precededByDigitOrPrefix: false, out var hasExponentDigit);

            if (!hasExponentDigit)
            {
                length = ExtendWithIdentifierContinue(text, i);
                return false;
            }
        }

        // Type suffixes and other identifier continuations are not supported.
        if ((uint)i < (uint)text.Length &&
            IsIdentifierContinue(text[i]))
        {
            length = ExtendWithIdentifierContinue(text, i);
            return false;
        }

        length = i;
        return true;
    }

    /// <summary>
    /// Determines whether the specified character is an ASCII decimal digit.
    /// </summary>
    /// <param name="c">The character to inspect.</param>
    /// <returns><see langword="true"/> if <paramref name="c"/> is in the range '0' through '9'; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDigit(char c)
        => (uint)(c - '0') <= 9;

    private static bool ScanBasedInteger(ReadOnlySpan<char> text, int start, int radix, out int length)
    {
        // Separators are allowed immediately after a radix prefix.
        var i = ScanDigitsAndSeparators(text, start, radix, precededByDigitOrPrefix: true, out _);

        // Reject invalid radix digits and unsupported suffixes.
        if ((uint)i < (uint)text.Length &&
            IsIdentifierContinue(text[i]))
        {
            // e.g. "0b102", "0x1g", "0x1i128"
            length = ExtendWithIdentifierContinue(text, i);
            return false;
        }

        length = i;
        return true;
    }

    private static int ScanDigitsAndSeparators(ReadOnlySpan<char> text, int i, int radix, bool precededByDigitOrPrefix, out bool hasDigit)
    {
        hasDigit = false;

        while ((uint)i < (uint)text.Length)
        {
            var c = text[i];

            if (IsDigit(c, radix))
            {
                hasDigit = true;
                precededByDigitOrPrefix = true;
                i++;
                continue;
            }

            if (c == '_' && precededByDigitOrPrefix)
            {
                i++;
                continue;
            }

            break;
        }

        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDigit(char c, int radix)
    {
        var value = (uint)(c - '0');

        if (value <= 9)
        {
            return value < (uint)radix;
        }

        return radix == 16 &&
            (uint)((c | 0x20) - 'a') <= 5;
    }

    private static int ExtendWithIdentifierContinue(ReadOnlySpan<char> text, int i)
    {
        while ((uint)i < (uint)text.Length &&
            IsIdentifierContinue(text[i]))
        {
            i++;
        }

        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierStart(char c)
    {
        return c == '_' ||
            (uint)(c - 'A') <= 25 ||
            (uint)(c - 'a') <= 25 ||
            c >= 0x80;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierContinue(char c)
        => IsIdentifierStart(c) || IsDigit(c);
}
