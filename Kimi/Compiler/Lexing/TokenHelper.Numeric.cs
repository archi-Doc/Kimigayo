// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Lexing;

public static partial class TokenHelper
{
    /// <summary>
    /// Scans a numeric literal at the start of <paramref name="text"/>.<br/>
    /// Returns <see langword="true"/> when a valid literal was found; <paramref name="length"/> is its length.<br/>
    /// Returns <see langword="false"/> with <paramref name="length"/> == 0 when the text does not start with a digit.<br/>
    /// Returns <see langword="false"/> with <paramref name="length"/> &gt; 0 when the text starts with a digit but does not
    /// form a valid literal (e.g. "0x", "1e+", "1.0f64", "123abc"); <paramref name="length"/> then covers the malformed
    /// literal so that the caller can emit a single Invalid token with a diagnostic.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="length">When this method returns, contains the number of characters consumed by the literal or malformed literal.</param>
    /// <returns><see langword="true"/> if a valid numeric literal was found; otherwise, <see langword="false"/>.</returns>
    public static bool ScanNumberLiteral(ReadOnlySpan<char> text, out int length)
    {
        length = 0;
        if (text.IsEmpty || !IsDigit(text[0]))
        {
            return false;
        }

        if (text.Length >= 2 && text[0] == '0')
        {
            // 0b..., 0o..., 0x...
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

        // A decimal literal is Int128 unless it contains a fraction or exponent,
        // in which case it is Double. Value conversion is performed after tokenization.
        var i = ScanDecDigitsOrUnderscores(text, 0);

        // Fraction part:
        // 1.0  => floating-point literal
        // 1.   => integer literal + dot
        // 1..2 => integer literal + range/operator
        // 1.foo => integer literal + member access
        if ((uint)i < (uint)text.Length &&
            text[i] == '.' &&
            i + 1 < text.Length &&
            IsDigit(text[i + 1]))
        {
            i = ScanDecDigitsOrUnderscores(text, i + 1);
        }

        // Exponent part. The first character after 'e'/'E' and an optional sign
        // must be a digit; underscores are allowed only after that first digit.
        if ((uint)i < (uint)text.Length && (text[i] | 0x20) == 'e')
        {
            i++;
            if ((uint)i < (uint)text.Length)
            {
                var c = text[i];
                if (c == '+' || c == '-')
                {
                    i++;
                }
            }

            if ((uint)i >= (uint)text.Length || !IsDigit(text[i]))
            {
                // e.g. "1e", "1e+", "1e_2", "1e+x"
                length = ExtendWithIdentifierContinue(text, i);
                return false;
            }

            i = ScanDecDigitsOrUnderscores(text, i);
        }

        // Numeric type suffixes are not supported. Any identifier continuation
        // immediately following the literal makes the entire sequence invalid.
        if ((uint)i < (uint)text.Length && IsIdentifierContinue(text[i]))
        {
            // e.g. "123abc", "1i32", "1.0f64"
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
    {
        return (uint)(c - '0') <= 9;
    }

    /// <summary>
    /// Determines whether the specified character is an ASCII decimal digit or an underscore separator.
    /// </summary>
    /// <param name="c">The character to inspect.</param>
    /// <returns><see langword="true"/> if <paramref name="c"/> is a digit or '_'; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDigitOrNumericSeparator(char c)
    {
        return IsDigit(c) || c == '_';
    }

    private static bool ScanBasedInteger(ReadOnlySpan<char> text, int start, int numberBase, out int length)
    {
        // The first character after the base prefix must be a digit.
        // After that first digit, underscores may appear anywhere, including
        // consecutively or at the end of the literal.
        if ((uint)start >= (uint)text.Length || !IsBasedDigit(text[start], numberBase))
        {
            // e.g. "0x", "0x_", "0b2", "0o8", "0xg"
            length = ExtendInvalidBasedInteger(text, start);
            return false;
        }

        var i = start + 1;
        while ((uint)i < (uint)text.Length)
        {
            var c = text[i];
            if (c == '_' || IsBasedDigit(c, numberBase))
            {
                i++;
                continue;
            }

            break;
        }

        // Numeric type suffixes are not supported. This also rejects digits
        // that are invalid for the selected base, such as '2' in a binary literal.
        if ((uint)i < (uint)text.Length && IsIdentifierContinue(text[i]))
        {
            // e.g. "0x1g", "0b102", "0x1i128"
            length = ExtendWithIdentifierContinue(text, i);
            return false;
        }

        length = i;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsBasedDigit(char c, int numberBase)
    {
        if (numberBase == 2)
        {
            return c == '0' || c == '1';
        }

        if (numberBase == 8)
        {
            return (uint)(c - '0') <= 7;
        }

        // Hex
        return (uint)(c - '0') <= 9 || (uint)((c | 0x20) - 'a') <= 5;
    }

    private static int ExtendInvalidBasedInteger(ReadOnlySpan<char> text, int i)
    {
        while ((uint)i < (uint)text.Length)
        {
            var c = text[i];

            if (IsIdentifierContinue(c) || c == '_')
            {
                i++;
                continue;
            }

            break;
        }

        return i;
    }

    private static int ScanDecDigitsOrUnderscores(ReadOnlySpan<char> text, int i)
    {
        while ((uint)i < (uint)text.Length)
        {
            var c = text[i];

            if (IsDigit(c) || c == '_')
            {
                i++;
                continue;
            }

            break;
        }

        return i;
    }

    /// <summary>
    /// Extends <paramref name="i"/> over any trailing identifier-continue characters so that a malformed
    /// numeric literal is reported as a single token (e.g. "1.0u8" instead of "1" + "." + "0u8").
    /// </summary>
    /// <param name="text">The text that contains the malformed numeric literal.</param>
    /// <param name="i">The first character position to test for identifier continuation.</param>
    /// <returns>The first index after the trailing identifier-continue sequence.</returns>
    private static int ExtendWithIdentifierContinue(ReadOnlySpan<char> text, int i)
    {
        while ((uint)i < (uint)text.Length && IsIdentifierContinue(text[i]))
        {
            i++;
        }

        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierStart(char c)
    {
        return c == '_' || (uint)(c - 'A') <= 25 || (uint)(c - 'a') <= 25 || c >= 0x80;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierContinue(char c)
    {
        return IsIdentifierStart(c) || IsDigit(c);
    }
}
