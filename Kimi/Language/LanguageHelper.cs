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
}
