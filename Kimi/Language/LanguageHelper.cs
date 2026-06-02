// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

using System.Buffers;
using System.Runtime.CompilerServices;
using Arc.Collections;

public static class LanguageHelper
{
    public static IReadOnlyDictionary<TokenKind, string> KeywordKindToKeyword => _keywordKindToKeyword;

    public static readonly Utf16Hashtable<TokenKind> KeywordToKeywordKind;

    private static readonly Dictionary<TokenKind, string> _keywordKindToKeyword;

    private static readonly SearchValues<char> Separators = SearchValues.Create(
    [// Separator Space, (, ), Cr, Lf, =, <, >, +, -, %, &, |, ','
        ' ', '\t', '\r', '\n',
        '(', ')', '{', '}', '[', ']',
        '.', ',', ';', ':', '?',
        '+', '-', '*', '/', '%',
        '&', '|', '^', '!', '~',
        '=', '<', '>',
    ]);

    static LanguageHelper()
    {
        _keywordKindToKeyword = new();
        KeywordToKeywordKind = new();
        foreach (var x in Enum.GetValues<TokenKind>())
        {
            if (x == TokenKind.None)
            {
                continue;
            }
            else if (x == TokenKind.Identifier)
            {// Anything after TokenKind.Identifier is not a keyword.
                break;
            }

            string keyword;
            if (x == TokenKind.ElseIf)
            {
                keyword = "else if";
            }
            else
            {
                keyword = x.ToString().ToLower();
            }

            _keywordKindToKeyword[x] = keyword;
            KeywordToKeywordKind.TryAdd(keyword, x);
        }
    }

    public static bool IsDecimalNumberStart(ReadOnlySpan<char> text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        var c = text[0];
        if (IsDigit(c))
        {
            return true;
        }

        // Handles floating-point literals such as .3, .3d, .3f, and .3m.
        return c == '.' && text.Length >= 2 && IsDigit(text[1]);
    }

    public static int ScanDecimalNumber(ReadOnlySpan<char> text)
    {
        var i = 0;

        // Integer part.
        if (i < text.Length && IsDigit(text[i]))
        {
            i++;
            while (i < text.Length && IsDigitOrSeparator(text[i]))
            {
                i++;
            }
        }

        // Fractional part.
        if (i < text.Length && text[i] == '.')
        {
            // Handles forms such as 1., 1.23, and .3.
            i++;
            while (i < text.Length && IsDigitOrSeparator(text[i]))
            {
                i++;
            }
        }

        // Exponent part: e+10, e-10, or E10.
        if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
        {
            var exponentStart = i;
            i++;
            if (i < text.Length && (text[i] == '+' || text[i] == '-'))
            {
                i++;
            }

            var digitStart = i;
            while (i < text.Length && IsDigitOrSeparator(text[i]))
            {
                i++;
            }

            // If no digits follow 'e' or 'E', treat it as not being an exponent.
            if (digitStart == i)
            {
                i = exponentStart;
            }
        }

        // Type suffix: f/F, d/D, or m/M.
        if (i < text.Length)
        {
            var suffix = text[i];
            if (suffix is 'f' or 'F' or 'd' or 'D' or 'm' or 'M')
            {
                i++;
            }
        }

        return i;
    }

    public static bool TryGetStringLiteralLength(ReadOnlySpan<char> text, out int length)
    {
        length = 0;
        if (text.IsEmpty || text[0] != '"')
        {
            return false;
        }

        var quoteCount = CountQuotesAt(text, 0);
        if (quoteCount >= 3)
        {
            return TryGetRawStringLiteralLength(text, quoteCount, out length);
        }

        return TryGetRegularStringLiteralLength(text, out length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDigit(char c)
    {
        return (uint)(c - '0') <= 9;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDigitOrSeparator(char c)
    {
        return IsDigit(c) || c == '_';
    }

    public static int IndexOfSeparator(ReadOnlySpan<char> text)
        => text.IndexOfAny(Separators);

    public static TokenKind CharToSingleToken(char c) => c switch
    {
        Constants.DotChar => TokenKind.Dot, // .
        Constants.CommaChar => TokenKind.Comma, // ,
        Constants.OpenBracketChar => TokenKind.OpenBracket, // [
        Constants.CloseBracketChar => TokenKind.CloseBracket, // ]
        Constants.OpenParenthesisChar => TokenKind.OpenParenthesis, // (
        Constants.CloseParenthesisChar => TokenKind.CloseParenthesis, // )
        Constants.ColonChar => TokenKind.Colon, // :
        Constants.SemicolonChar => TokenKind.Semicolon, // ;
        Constants.DollarChar => TokenKind.Dollar, // $
        Constants.TildeChar => TokenKind.Tilde, // ~
        _ => TokenKind.None,
    };

    public static TokenKind GetSingleCharTokenKind(char c)
    {
        return c switch
        {
            '~' => TokenKind.Tilde,
            '!' => TokenKind.Exclamation,
            '$' => TokenKind.Dollar,
            '%' => TokenKind.Percent,
            '^' => TokenKind.Caret,
            '&' => TokenKind.Ampersand,
            '*' => TokenKind.Asterisk,
            '(' => TokenKind.OpenParenthesis,
            ')' => TokenKind.CloseParenthesis,
            '-' => TokenKind.Minus,
            '+' => TokenKind.Plus,
            '=' => TokenKind.Equals,
            '[' => TokenKind.OpenBracket,
            ']' => TokenKind.CloseBracket,
            '|' => TokenKind.Bar,
            ':' => TokenKind.Colon,
            ';' => TokenKind.Semicolon,
            '<' => TokenKind.LessThan,
            ',' => TokenKind.Comma,
            '>' => TokenKind.GreaterThan,
            '.' => TokenKind.Dot,
            '/' => TokenKind.Slash,

            _ => TokenKind.None,
        };
    }

    private static bool TryGetRegularStringLiteralLength(ReadOnlySpan<char> text, out int length)
    {
        length = 0;

        // The caller has already verified that the first character is '"'.
        var i = 1;
        while (i < text.Length)
        {
            var c = text[i];

            // A regular string literal cannot contain a physical line break.
            if (c == '\r' || c == '\n')
            {
                return false;
            }

            // Skip escaped character, such as \" or \\.
            if (c == '\\')
            {
                i++;
                if (i >= text.Length)
                {
                    return false;
                }

                i++;
                continue;
            }

            // Closing quote.
            if (c == '"')
            {
                length = i + 1;
                return true;
            }

            i++;
        }

        return false;
    }

    private static bool TryGetRawStringLiteralLength(ReadOnlySpan<char> text, int delimiterQuoteCount, out int length)
    {
        length = 0;

        // Raw string literals use at least three quotes as the delimiter.
        var i = delimiterQuoteCount;
        while (i < text.Length)
        {
            if (text[i] != '"')
            {
                i++;
                continue;
            }

            var quoteCount = CountQuotesAt(text, i);

            // The closing delimiter must have at least the same number of quotes
            // as the opening delimiter.
            if (quoteCount >= delimiterQuoteCount)
            {
                length = i + delimiterQuoteCount;
                return true;
            }

            i += quoteCount;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountQuotesAt(ReadOnlySpan<char> text, int start)
    {
        var i = start;
        while (i < text.Length && text[i] == '"')
        {
            i++;
        }

        return i - start;
    }
}
