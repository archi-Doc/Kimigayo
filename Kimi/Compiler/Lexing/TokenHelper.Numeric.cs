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
            switch ((char)(text[1] | 0x20))
            {
                case 'b':
                    return ScanBasedInteger(text, 2, 2, out length);

                case 'o':
                    return ScanBasedInteger(text, 2, 8, out length);

                case 'x':
                    return ScanBasedInteger(text, 2, 16, out length);
            }
        }

        // The first decimal digit has already been validated. Therefore,
        // separators are allowed from the current position onward.
        var i = ScanDigitsAndSeparators(text, 1, 10);

        // A fractional part is recognized only when the decimal point is
        // immediately followed by a digit:
        //
        // 1.0  => floating-point literal
        // 1.   => integer literal + dot
        // 1._2 => integer literal + dot + identifier
        if ((uint)i < (uint)text.Length &&
            text[i] == '.' &&
            (uint)(i + 1) < (uint)text.Length &&
            IsDigit(text[i + 1]))
        {
            // The first fractional digit has already been validated.
            i = ScanDigitsAndSeparators(text, i + 2, 10);
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

            // The exponent must start with a digit. Separators cannot
            // immediately follow 'e', 'E', '+' or '-'.
            if ((uint)i >= (uint)text.Length || !IsDigit(text[i]))
            {
                length = ExtendWithIdentifierContinue(text, i);
                return false;
            }

            // The first exponent digit has already been validated.
            i = ScanDigitsAndSeparators(text, i + 1, 10);
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
    /// <returns>
    /// <see langword="true"/> if <paramref name="c"/> is in the range
    /// '0' through '9'; otherwise, <see langword="false"/>.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDigit(char c)
        => (uint)(c - '0') <= 9;

    private static bool ScanBasedInteger(ReadOnlySpan<char> text, int start, int radix, out int length)
    {
        // Separators are allowed immediately after the radix prefix.
        // A prefix followed by no digits, such as "0x" or "0x____",
        // represents zero.
        var i = ScanDigitsAndSeparators(text, start, radix);

        // Reject digits that are invalid for the radix, identifier characters
        // and unsupported type suffixes.
        if ((uint)i < (uint)text.Length &&
            IsIdentifierContinue(text[i]))
        {
            // e.g. "0b102", "0o8", "0x1g", "0x1i128"
            length = ExtendWithIdentifierContinue(text, i);
            return false;
        }

        length = i;
        return true;
    }

    /// <summary>
    /// Scans digits of the specified radix and underscore separators.
    /// </summary>
    /// <remarks>
    /// The caller must ensure that a digit or radix prefix precedes
    /// <paramref name="i"/>, allowing separators at that position.
    /// </remarks>
    private static int ScanDigitsAndSeparators(ReadOnlySpan<char> text, int i, int radix)
    {
        while ((uint)i < (uint)text.Length)
        {
            var c = text[i];

            if (c != '_' && !IsRadixDigit(c, radix))
            {
                break;
            }

            i++;
        }

        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsRadixDigit(char c, int radix)
    {
        var value = (uint)(c - '0');
        if (value <= 9)
        {
            return value < (uint)radix;
        }

        return radix == 16 &&
            (uint)((c | 0x20) - 'a') <= 5;
    }

    /// <summary>
    /// Extends <paramref name="i"/> over trailing identifier-continue
    /// characters so that a malformed numeric literal is reported as a
    /// single token.
    /// </summary>
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
