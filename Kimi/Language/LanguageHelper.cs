// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

using System.Drawing;
using Arc.Collections;

public static class LanguageHelper
{
    public static IReadOnlyDictionary<TokenKind, string> KeywordKindToKeyword => _keywordKindToKeyword;

    public static readonly Utf16Hashtable<TokenKind> KeywordToKeywordKind;

    private static readonly Dictionary<TokenKind, string> _keywordKindToKeyword;

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
