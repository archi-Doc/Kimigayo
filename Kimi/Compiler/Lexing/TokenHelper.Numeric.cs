// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Kimi.Compiler.Lexing;

public static partial class TokenHelper
{
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

        // The first decimal digit has already been validated.
        // Therefore, separators are allowed from index 1 onward.
        var i = ScanDecimalDigitsAndSeparators(text, 1);

        // A fractional part requires at least one digit after the decimal point.
        //
        // 1.0  => floating-point literal
        // 1.   => integer literal followed by a dot
        // 1._2 => integer literal followed by a dot and an identifier
        if ((uint)i < (uint)textLength && text[i] == '.')
        {
            var fractionStart = i + 1;

            if ((uint)fractionStart < (uint)textLength &&
                (uint)(text[fractionStart] - '0') <= 9u)
            {
                // The first fractional digit has already been validated.
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

            // The first exponent digit has already been validated.
            i = ScanDecimalDigitsAndSeparators(text, i + 1);
        }

        return FinishNumberLiteral(text, i, out length);
    }

    /// <summary>
    /// Completes a numeric-literal scan and rejects unsupported suffixes and
    /// other identifier continuations.
    /// </summary>
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

    /// <summary>
    /// Scans binary digits and underscore separators.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ScanBinaryDigitsAndSeparators(ReadOnlySpan<char> text, int i)
    {
        var textLength = text.Length;
        while ((uint)i < (uint)textLength)
        {
            var c = text[i];

            // '0', '1' or '_'.
            //
            // The digit test is performed first because digits are expected
            // to be substantially more common than separators.
            if ((uint)(c - '0') > 1u && c != '_')
            {
                break;
            }

            i++;
        }

        return i;
    }

    /// <summary>
    /// Scans octal digits and underscore separators.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ScanOctalDigitsAndSeparators(ReadOnlySpan<char> text, int i)
    {
        var textLength = text.Length;
        while ((uint)i < (uint)textLength)
        {
            var c = text[i];

            // '0' through '7' or '_'.
            if ((uint)(c - '0') > 7u && c != '_')
            {
                break;
            }

            i++;
        }

        return i;
    }

    /// <summary>
    /// Scans decimal digits and underscore separators.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ScanDecimalDigitsAndSeparators(ReadOnlySpan<char> text, int i)
    {
        var textLength = text.Length;
        while ((uint)i < (uint)textLength)
        {
            var c = text[i];

            // '0' through '9' or '_'.
            if ((uint)(c - '0') > 9u && c != '_')
            {
                break;
            }

            i++;
        }

        return i;
    }

    /// <summary>
    /// Scans hexadecimal digits and underscore separators.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ScanHexadecimalDigitsAndSeparators(ReadOnlySpan<char> text, int i)
    {
        var textLength = text.Length;
        while ((uint)i < (uint)textLength)
        {
            var c = text[i];

            // '0' through '9', 'A' through 'F', 'a' through 'f' or '_'.
            //
            // OR-ing with 0x20 folds ASCII uppercase letters to lowercase.
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

    /// <summary>
    /// Extends <paramref name="i"/> over identifier-continue characters so
    /// that a malformed numeric literal is reported as a single token.
    /// </summary>
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
