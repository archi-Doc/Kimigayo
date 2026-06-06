// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Runtime.CompilerServices;
using Arc.Collections;
using Kimi.Language;
using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

public static class TokenHelper
{
    public static IReadOnlyDictionary<TokenKind, string> KeywordKindToKeyword => _keywordKindToKeyword;

    public static readonly Utf16Hashtable<TokenKind> KeywordToKeywordKind;

    private static readonly Dictionary<TokenKind, string> _keywordKindToKeyword;

    private static readonly SearchValues<char> Separators = SearchValues.Create(
    [// Separator Space, (, ), Cr, Lf, =, <, >, +, -, %, &, |, ',', #
        ' ', '\t', '\r', '\n',
        '(', ')', '{', '}', '[', ']',
        '.', ',', ';', ':', '?',
        '+', '-', '*', '/', '%',
        '&', '|', '^', '!', '~',
        '=', '<', '>', '#',
    ]);

    static TokenHelper()
    {
        _keywordKindToKeyword = new();
        KeywordToKeywordKind = new();
        foreach (var x in Enum.GetValues<TokenKind>())
        {
            if (x == TokenKind.Invalid)
            {
                continue;
            }
            else if (x == TokenKind.Identifier)
            {// Anything after TokenKind.Identifier is not a keyword.
                break;
            }

            var keyword = x.ToString().ToLower();
            _keywordKindToKeyword[x] = keyword;
            KeywordToKeywordKind.TryAdd(keyword, x);
        }
    }

    public static bool IsGroup(this StatementContext statementContext) => statementContext switch
    {
        StatementContext.Namespace => true,
        StatementContext.Group => true,
        StatementContext.Struct => true,
        StatementContext.Enum => true,
        _ => false,
    };

    public static bool ScanNumberLiteral(ReadOnlySpan<char> text, out int length)
    {
        length = 0;
        if ((uint)text.Length == 0 || !IsDecDigit(text[0]))
        {
            return false;
        }

        var i = 0;
        if (text.Length >= 2 && text[0] == '0')
        {// 0b..., 0o..., 0x...
            var p = text[1];
            if ((p | 0x20) == 'b')
            {
                return ScanBasedInteger(text, 2, 2, out length);
            }

            if ((p | 0x20) == 'o')
            {
                return ScanBasedInteger(text, 2, 8, out length);
            }

            if ((p | 0x20) == 'x')
            {
                return ScanBasedInteger(text, 2, 16, out length);
            }
        }

        // Decimal integer part.
        i = ScanDecDigitsOrUnderscores(text, 0);
        var isFloat = false;

        // Fraction part.
        // 1.0  => float
        // 1.   => float
        // 1..2 => integer literal "1"
        // 1.foo => integer literal "1"
        if ((uint)i < (uint)text.Length && text[i] == '.')
        {
            char next = (i + 1 < text.Length) ? text[i + 1] : '\0';

            if (next != '.' && next != '_' && !IsIdentifierStart(next))
            {
                isFloat = true;
                i++;
                i = ScanDecDigitsOrUnderscores(text, i);
            }
        }

        // Exponent part.
        if ((uint)i < (uint)text.Length)
        {
            var c = text[i];
            if ((c | 0x20) == 'e')
            {
                i++;
                if ((uint)i < (uint)text.Length)
                {
                    c = text[i];
                    if (c == '+' || c == '-')
                    {
                        i++;
                    }
                }

                var hasDigit = false;
                while ((uint)i < (uint)text.Length)
                {
                    c = text[i];

                    if (IsDecDigit(c))
                    {
                        hasDigit = true;
                        i++;
                        continue;
                    }

                    if (c == '_')
                    {
                        i++;
                        continue;
                    }

                    break;
                }

                if (!hasDigit)
                {
                    length = 0;
                    return false;
                }

                isFloat = true;
            }
        }

        int suffixLength = ScanSuffix(text.Slice(i), isFloat);
        if (suffixLength < 0)
        {
            length = 0;
            return false;
        }

        i += suffixLength;

        if ((uint)i < (uint)text.Length && IsIdentifierContinue(text[i]))
        {
            length = 0;
            return false;
        }

        length = i;
        return true;
    }

    private static bool ScanBasedInteger(ReadOnlySpan<char> text, int start, int numberBase, out int length)
    {
        var i = start;
        var hasDigit = false;
        while ((uint)i < (uint)text.Length)
        {
            var c = text[i];
            if (c == '_')
            {
                i++;
                continue;
            }

            if (numberBase == 2)
            {
                if (c != '0' && c != '1')
                {
                    break;
                }
            }
            else if (numberBase == 8)
            {
                if ((uint)(c - '0') > 7)
                {
                    break;
                }
            }
            else
            {// Hex
                if ((uint)(c - '0') > 9 && (uint)((c | 0x20) - 'a') > 5)
                {
                    break;
                }
            }

            hasDigit = true;
            i++;
        }

        if (!hasDigit)
        {
            length = 0;
            return false;
        }

        int suffixLength = ScanSuffix(text.Slice(i), isFloat: false);
        if (suffixLength < 0)
        {
            length = 0;
            return false;
        }

        i += suffixLength;
        if ((uint)i < (uint)text.Length && IsIdentifierContinue(text[i]))
        {
            length = 0;
            return false;
        }

        length = i;
        return true;
    }

    private static int ScanDecDigitsOrUnderscores(ReadOnlySpan<char> text, int i)
    {
        while ((uint)i < (uint)text.Length)
        {
            char c = text[i];

            if (IsDecDigit(c) || c == '_')
            {
                i++;
                continue;
            }

            break;
        }

        return i;
    }

    private static int ScanSuffix(ReadOnlySpan<char> text, bool isFloat)
    {
        if (text.IsEmpty)
        {
            return 0;
        }

        char c0 = text[0];

        if (isFloat)
        {
            if (text.Length >= 3 &&
                c0 == 'f' &&
                ((text[1] == '3' && text[2] == '2') ||
                (text[1] == '6' && text[2] == '4')))
            {
                return 3; // f32 / f64
            }

            return IsIdentifierStart(c0) ? -1 : 0;
        }

        if (text.Length >= 2 &&
            (c0 == 'u' || c0 == 'i') &&
            text[1] == '8')
        {// u8 / i8
            return 2;
        }

        if (text.Length >= 3 &&
            (c0 == 'u' || c0 == 'i'))
        {// u16 / i16 / u32 / i32 / u64 / i64
            var c1 = text[1];
            var c2 = text[2];
            if ((c1 == '1' && c2 == '6') ||
                (c1 == '3' && c2 == '2') ||
                (c1 == '6' && c2 == '4'))
            {
                return 3;
            }
        }

        // u128 / i128
        if (text.Length >= 4 &&
            (c0 == 'u' || c0 == 'i') &&
            text[1] == '1' &&
            text[2] == '2' &&
            text[3] == '8')
        {
            return 4;
        }

        // usize / isize
        if (text.Length >= 5 &&
            (c0 == 'u' || c0 == 'i') &&
            text[1] == 's' &&
            text[2] == 'i' &&
            text[3] == 'z' &&
            text[4] == 'e')
        {
            return 5;
        }

        return IsIdentifierStart(c0) ? -1 : 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDecDigit(char c)
    {
        return (uint)(c - '0') <= 9;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierStart(char c)
    {
        return c == '_' || (uint)(c - 'A') <= 25 || (uint)(c - 'a') <= 25 || c >= 0x80;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsIdentifierContinue(char c)
    {
        return IsIdentifierStart(c) || IsDecDigit(c);
    }

    public static bool ScanStringLiteral(ReadOnlySpan<char> text, out int length, out int quoteCount)
    {
        length = 0;
        quoteCount = 0;
        if (text.IsEmpty || text[0] != '"')
        {
            return false;
        }

        quoteCount = CountQuotesAt(text, 0);
        if (quoteCount >= 3)
        {
            TryGetRawStringLiteralLength(text, quoteCount, out length);
        }
        else
        {
            TryGetRegularStringLiteralLength(text, out length);
        }

        return true;
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
        _ => TokenKind.Invalid,
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
            '{' => TokenKind.OpenBrace,
            '}' => TokenKind.CloseBrace,
            '|' => TokenKind.Bar,
            ':' => TokenKind.Colon,
            ';' => TokenKind.Semicolon,
            '<' => TokenKind.LessThan,
            ',' => TokenKind.Comma,
            '>' => TokenKind.GreaterThan,
            '.' => TokenKind.Dot,
            '/' => TokenKind.Slash,

            _ => TokenKind.Invalid,
        };
    }

    public static bool IsBlockToken(TokenKind tokenKind)
        => tokenKind >= TokenKind.Group && tokenKind <= TokenKind.Match;

    public static bool TryGetSingleCharTokenKind(char c, out TokenKind tokenKind, out int groupingDepth)
    {
        (tokenKind, groupingDepth) = c switch
        {
            // Constants.DotChar => (TokenKind.Dot, 0),
            Constants.CommaChar => (TokenKind.Comma, 0),
            Constants.SharpChar => (TokenKind.Sharp, 0),
            Constants.OpenBracketChar => (TokenKind.OpenBracket, +1),
            Constants.CloseBracketChar => (TokenKind.CloseBracket, -1),
            Constants.OpenParenthesisChar => (TokenKind.OpenParenthesis, +1),
            Constants.CloseParenthesisChar => (TokenKind.CloseParenthesis, -1),
            Constants.OpenBraceChar => (TokenKind.OpenBrace, +1),
            Constants.CloseBraceChar => (TokenKind.CloseBrace, -1),
            Constants.ColonChar => (TokenKind.Colon, 0),
            Constants.SemicolonChar => (TokenKind.Semicolon, 0),
            Constants.DollarChar => (TokenKind.Dollar, 0),
            Constants.TildeChar => (TokenKind.Tilde, 0),
            Constants.AmpersandChar => (TokenKind.Ampersand, 0),
            Constants.AsteriskChar => (TokenKind.Asterisk, 0),
            Constants.BarChar => (TokenKind.Bar, 0),
            Constants.CaretChar => (TokenKind.Caret, 0),
            Constants.EqualsChar => (TokenKind.Equals, 0),
            Constants.ExclamationChar => (TokenKind.Exclamation, 0),
            Constants.GreaterThanChar => (TokenKind.GreaterThan, 0),
            Constants.LessThanChar => (TokenKind.LessThan, 0),
            Constants.MinusChar => (TokenKind.Minus, 0),
            Constants.PercentChar => (TokenKind.Percent, 0),
            Constants.PlusChar => (TokenKind.Plus, 0),
            Constants.SlashChar => (TokenKind.Slash, 0),
            Constants.QuestionChar => (TokenKind.Question, 0),
            _ => (TokenKind.Invalid, 0),
        };

        return tokenKind != TokenKind.Invalid;
    }

    private static void TryGetRegularStringLiteralLength(ReadOnlySpan<char> text, out int length)
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
                length = -1;
                return;
            }

            // Skip escaped character, such as \" or \\.
            if (c == '\\')
            {
                i++;
                if (i >= text.Length)
                {
                    length = -1;
                    return;
                }

                i++;
                continue;
            }

            // Closing quote.
            if (c == '"')
            {
                length = i + 1;
                return;
            }

            i++;
        }

        length = -1;
    }

    private static void TryGetRawStringLiteralLength(ReadOnlySpan<char> text, int delimiterQuoteCount, out int length)
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
                // length = i + delimiterQuoteCount;
                length = i + delimiterQuoteCount;
                return;
            }

            i += quoteCount;
        }

        length = -1;
        return;
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

internal sealed class Tokenizer
{
    #region FieldAndProperty

    private readonly KimiControl kimiControl;
    private readonly UrlDiagnostic urlDiagnostic;

    private ReadOnlyMemory<char> text;
    private int position;
    private int line;
    private int character;

    private int numberOfBlocks;
    private int numberOfBrackets;

    private List<Token> tokenList = new();
    private int numberOfTokens;

    #endregion

    public Tokenizer(KimiControl kimiControl, UrlDiagnostic urlDiagnostic)
    {
        this.kimiControl = kimiControl;
        this.urlDiagnostic = urlDiagnostic;
    }

    public void Initialize(ReadOnlyMemory<char> text, int line, int character)
    {
        this.text = text;
        this.position = 0;
        this.line = line;
        this.character = character;

        this.numberOfBlocks = -1;
        this.numberOfBrackets = 0;
        this.ClearToken();
    }

    public (List<Token> List, int Count) Read()
    {
        this.ClearToken();
Loop:
        var span = this.text.Slice(this.position).Span;
        if (span.Length == 0)
        {// End-of-file
            goto EndOfFile;
        }

        // Skip spaces
        var numberOfSpaces = Arc.BaseHelper.CountLeadingSpaces(span);
        this.Slice(ref span, numberOfSpaces);

        if (span[0] == Constants.LfChar)
        {// Empty line (\n)
            this.Slice(ref span, 1);
            this.NextLine();
            goto Loop;
        }
        else if (span.Length >= 2)
        {
            if (span[0] == Constants.CrChar && span[1] == Constants.LfChar)
            {// Empty line (\r\n)
                this.Slice(ref span, 2);
                this.NextLine();
                goto Loop;
            }
            else if (span[0] == Constants.SlashChar)
            {// /
                if (span[1] == Constants.SlashChar)
                {// //
                    if (this.ReadSingleLineComment(ref span))
                    {
                        this.NextLine();
                    }

                    goto NextLine;
                }
                else if (span[1] == Constants.AsteriskChar)
                {// /*
                    var lineFeeds = this.ReadMultiLineComment(ref span);
                    if (lineFeeds > 0)
                    {
                        numberOfSpaces = 0;
                    }
                }
            }
        }

        var unnecessarySpaces = numberOfSpaces % Constants.IndentationSpaces;
        if (unnecessarySpaces > 0)
        {// Invalid indentation
            numberOfSpaces += Constants.IndentationSpaces - unnecessarySpaces;
            this.urlDiagnostic.Add(new(new(this.line, 0), new(this.line, this.character)), Hashed.Parser.InvalidIndentation, Constants.IndentationSpaces);
        }

        var currentIndents = numberOfSpaces / Constants.IndentationSpaces;
        if (this.numberOfBlocks < 0)
        {
            this.numberOfBlocks = currentIndents - this.numberOfBrackets;
        }
        else if (this.numberOfBrackets == 0)
        {
            var dif = currentIndents - this.numberOfBlocks + this.numberOfBrackets;
            if (dif > 0)
            {
                do
                {
                    this.numberOfBlocks++;
                    this.AddToken(new(TokenKind.StartBlock, default));
                }
                while (--dif > 0);
            }
            else if (dif < 0)
            {
                do
                {
                    this.numberOfBlocks--;
                    this.AddToken(new(TokenKind.EndBlock, default));
                }
                while (++dif < 0);
            }
        }

        while (span.Length > 0)
        {
            while (span[0] == Constants.SpaceChar)
            {// Skip spaces
                this.Slice(ref span, 1);
                if (span.Length == 0)
                {// End-of-file
                    goto EndOfFile;
                }
            }

            if (span.Length == 0)
            {// End-of-file
                goto EndOfFile;
            }

            // span.Length >= 1
            switch (span[0])
            {
                case Constants.CrChar:
                    if (span.Length > 1 && span[1] == Constants.LfChar)
                    {// \r\n
                        this.Slice(ref span, 2);
                        this.NextLine();
                        if (this.numberOfBrackets == 0)
                        {
                            return (this.tokenList, this.numberOfTokens);
                        }
                        else
                        {
                            goto NextLine;
                        }
                    }
                    else
                    {// \r
                        this.Slice(ref span, 1);
                        this.NextLine();
                        if (this.numberOfBrackets == 0)
                        {
                            return (this.tokenList, this.numberOfTokens);
                        }
                        else
                        {
                            goto NextLine;
                        }
                    }

                case Constants.LfChar: // \n
                    this.Slice(ref span, 1);
                    this.NextLine();
                    if (this.numberOfBrackets == 0)
                    {
                        return (this.tokenList, this.numberOfTokens);
                    }
                    else
                    {
                        goto NextLine;
                    }

                case Constants.AmpersandChar: // && &= &
                    if (span.Length == 1)
                    {// &
                        this.AddTokenAndSlice(TokenKind.Ampersand, ref span, 1);
                    }
                    else if (span[1] == Constants.AmpersandChar)
                    {// &&
                        this.AddTokenAndSlice(TokenKind.AmpersandAmpersand, ref span, 2);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// &=
                        this.AddTokenAndSlice(TokenKind.AmpersandEquals, ref span, 2);
                    }
                    else
                    {// &
                        this.AddTokenAndSlice(TokenKind.Ampersand, ref span, 1);
                    }

                    break;

                case Constants.AsteriskChar: // * *=
                    if (span.Length > 1 && span[1] == Constants.EqualsChar)
                    {// *=
                        this.AddTokenAndSlice(TokenKind.AsteriskEquals, ref span, 2);
                    }
                    else
                    {// *
                        this.AddTokenAndSlice(TokenKind.Asterisk, ref span, 1);
                    }

                    break;

                case Constants.BarChar: // | || |=
                    if (span.Length == 1)
                    {// |
                        this.AddTokenAndSlice(TokenKind.Bar, ref span, 1);
                    }
                    else if (span[1] == Constants.BarChar)
                    {// ||
                        this.AddTokenAndSlice(TokenKind.BarBar, ref span, 2);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// |=
                        this.AddTokenAndSlice(TokenKind.BarEquals, ref span, 2);
                    }
                    else
                    {// |
                        this.AddTokenAndSlice(TokenKind.Bar, ref span, 1);
                    }

                    break;

                case Constants.CaretChar: // ^ ^=
                    if (span.Length > 1 && span[1] == Constants.EqualsChar)
                    {// ^=
                        this.AddTokenAndSlice(TokenKind.CaretEquals, ref span, 2);
                    }
                    else
                    {// ^
                        this.AddTokenAndSlice(TokenKind.Caret, ref span, 1);
                    }

                    break;

                case Constants.DotChar: // . .. ..=
                    if (span.Length == 1)
                    {// .
                        this.AddTokenAndSlice(TokenKind.Dot, ref span, 1);
                    }
                    else if (span[1] == Constants.DotChar)
                    {// ..
                        if (span.Length >= 3 && span[2] == Constants.EqualsChar)
                        {// ..=
                            this.AddTokenAndSlice(TokenKind.DotDotEquals, ref span, 3);
                        }
                        else
                        {// ..
                            this.AddTokenAndSlice(TokenKind.DotDot, ref span, 2);
                        }
                    }
                    else
                    {// .
                        this.AddTokenAndSlice(TokenKind.Dot, ref span, 1);
                    }

                    break;

                case Constants.EqualsChar: // = == =>
                    if (span.Length == 1)
                    {// =
                        this.AddTokenAndSlice(TokenKind.Equals, ref span, 1);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// ==
                        this.AddTokenAndSlice(TokenKind.EqualsEquals, ref span, 2);
                    }
                    else if (span[1] == Constants.GreaterThanChar)
                    {// =>
                        this.AddTokenAndSlice(TokenKind.EqualsGreaterThan, ref span, 2);
                    }
                    else
                    {// =
                        this.AddTokenAndSlice(TokenKind.Equals, ref span, 1);
                    }

                    break;

                case Constants.ExclamationChar: // ! !=
                    if (span.Length > 1 && span[1] == Constants.EqualsChar)
                    {// !=
                        this.AddTokenAndSlice(TokenKind.ExclamationEquals, ref span, 2);
                    }
                    else
                    {// !
                        this.AddTokenAndSlice(TokenKind.Exclamation, ref span, 1);
                    }

                    break;

                case Constants.GreaterThanChar: // > >= >>=
                    if (span.Length == 1)
                    {// >
                        this.AddTokenAndSlice(TokenKind.GreaterThan, ref span, 1);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// >=
                        this.AddTokenAndSlice(TokenKind.GreaterThanEquals, ref span, 2);
                    }
                    else if (span[1] == Constants.GreaterThanChar)
                    {// >>
                        if (span.Length >= 3 && span[2] == Constants.EqualsChar)
                        {// >>=
                            this.AddTokenAndSlice(TokenKind.GreaterThanGreaterThanEquals, ref span, 3);
                        }
                        else
                        {// >>
                            this.AddTokenAndSlice(TokenKind.GreaterThanGreaterThan, ref span, 2);
                        }
                    }
                    else
                    {// >
                        this.AddTokenAndSlice(TokenKind.GreaterThan, ref span, 1);
                    }

                    break;

                case Constants.LessThanChar: // < <= <<=
                    if (span.Length == 1)
                    {// <
                        this.AddTokenAndSlice(TokenKind.LessThan, ref span, 1);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// <=
                        this.AddTokenAndSlice(TokenKind.LessThanEquals, ref span, 2);
                    }
                    else if (span[1] == Constants.LessThanChar)
                    {// <<
                        if (span.Length >= 3 && span[2] == Constants.EqualsChar)
                        {// <<=
                            this.AddTokenAndSlice(TokenKind.LessThanLessThanEquals, ref span, 3);
                        }
                        else
                        {// <<
                            this.AddTokenAndSlice(TokenKind.LessThanLessThan, ref span, 2);
                        }
                    }
                    else
                    {// <
                        this.AddTokenAndSlice(TokenKind.LessThan, ref span, 1);
                    }

                    break;

                case Constants.MinusChar: // -- -= -
                    if (span.Length == 1)
                    {// -
                        this.AddTokenAndSlice(TokenKind.Minus, ref span, 1);
                    }
                    else if (span[1] == Constants.MinusChar)
                    {// --
                        this.AddTokenAndSlice(TokenKind.MinusMinus, ref span, 2);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// -=
                        this.AddTokenAndSlice(TokenKind.MinusEquals, ref span, 2);
                    }
                    else
                    {// -
                        this.AddTokenAndSlice(TokenKind.Minus, ref span, 1);
                    }

                    break;

                case Constants.PercentChar: // % %=
                    if (span.Length > 1 && span[1] == Constants.EqualsChar)
                    {// %=
                        this.AddTokenAndSlice(TokenKind.PercentEquals, ref span, 2);
                    }
                    else
                    {// %
                        this.AddTokenAndSlice(TokenKind.Percent, ref span, 1);
                    }

                    break;

                case Constants.PlusChar: // ++ += +
                    if (span.Length == 1)
                    {// +
                        this.AddTokenAndSlice(TokenKind.Plus, ref span, 1);
                    }
                    else if (span[1] == Constants.PlusChar)
                    {// ++
                        this.AddTokenAndSlice(TokenKind.PlusPlus, ref span, 2);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// +=
                        this.AddTokenAndSlice(TokenKind.PlusEquals, ref span, 2);
                    }
                    else
                    {// +
                        this.AddTokenAndSlice(TokenKind.Plus, ref span, 1);
                    }

                    break;

                case Constants.SlashChar: // // /* /= /
                    if (span.Length == 1)
                    {// /
                        this.AddTokenAndSlice(TokenKind.Slash, ref span, 1);
                    }
                    else if (span[1] == Constants.SlashChar)
                    {// //
                        if (this.ReadSingleLineComment(ref span))
                        {
                            this.NextLine();
                        }

                        goto NextLine;
                    }
                    else if (span[1] == Constants.AsteriskChar)
                    {// /*
                        this.ReadMultiLineComment(ref span);
                    }
                    else if (span[1] == Constants.EqualsChar)
                    {// /=
                        this.AddTokenAndSlice(TokenKind.SlashEquals, ref span, 2);
                    }
                    else
                    {// /
                        this.AddTokenAndSlice(TokenKind.Slash, ref span, 1);
                    }

                    break;

                default:
                    {
                        if (TokenHelper.TryGetSingleCharTokenKind(span[0], out var tokenKind, out var depth))
                        {// Single char token
                            this.numberOfBrackets += depth;
                            if (this.numberOfBrackets < 0)
                            {
                                this.numberOfBrackets = 0;
                                this.urlDiagnostic.Add(this.NewRange(1), Hashed.Parser.UnmatchedClosingBracket);
                            }

                            this.AddTokenAndSlice(tokenKind, ref span, 1);
                        }
                        else if (TokenHelper.ScanNumberLiteral(span, out var numberLiteralLength))
                        {// Numeric literal
                         // If the current position starts a numeric literal, scan the entire numeric literal before checking separators.
                            this.AddTokenAndSlice(TokenKind.NumericLiteral, ref span, numberLiteralLength);
                        }
                        else if (TokenHelper.ScanStringLiteral(span, out var literalLength, out var quoteCount))
                        {// String literal
                            if (literalLength < 0)
                            {// Invalid literal
                                var invalidLength = BaseHelper.IndexOfLfOrCrLf(span, out _);
                                if (invalidLength < 0)
                                {
                                    invalidLength = span.Length;
                                }

                                this.urlDiagnostic.Add(this.NewRange(1), Hashed.Parser.MissingStringLiteralEnd);
                                this.AddTokenAndSlice(TokenKind.Invalid, ref span, invalidLength);
                            }
                            else
                            {
                                if (quoteCount > 1)
                                {
                                    this.AddTokenAndSliceWithLineTracking(TokenKind.Literal, ref span, literalLength);
                                }
                                else
                                {
                                    this.AddTokenAndSlice(TokenKind.Literal, ref span, literalLength);
                                }

                                // this.Slice(ref span, quoteCount);
                                // this.AddTokenAndSlice(TokenKind.Literal, ref span, literalLength - quoteCount - quoteCount);
                                // this.Slice(ref span, quoteCount);
                            }
                        }
                        else
                        {// Keyword or Identifier
                            var length = TokenHelper.IndexOfSeparator(span);
                            if (length < 0)
                            {
                                length = span.Length;
                            }
                            else if (length == 0)
                            {
                                this.urlDiagnostic.Add(this.NewRange(1), Hashed.Parser.InvalidCharacter, span[0]);
                                this.AddTokenAndSlice(TokenKind.Invalid, ref span, 1);
                                break;
                            }

                            if (TokenHelper.KeywordToKeywordKind.TryGetValue(span.Slice(0, length), out var tokenKind2))
                            {// Keyword
                             // this.requiresIndent = LanguageHelper.RequiresImplicitIndentation(tokenKind2);
                                this.AddTokenAndSlice(tokenKind2, ref span, length);
                            }
                            else
                            {// Identifier
                                this.AddTokenAndSlice(TokenKind.Identifier, ref span, length);
                            }
                        }

                        break;
                    }
            }
        }

NextLine:
        if (span.Length == 0)
        {// End-of-file
            goto EndOfFile;
        }

        if (this.numberOfBrackets == 0)
        {
            return (this.tokenList, this.numberOfTokens);
        }

        goto Loop;

EndOfFile:
        while (this.numberOfBlocks > 0)
        {
            this.numberOfBlocks--;
            this.AddToken(new(TokenKind.EndBlock, default));
        }

        return (this.tokenList, this.numberOfTokens);
    }

    private int ReadMultiLineComment(ref ReadOnlySpan<char> text)
    {
        var length = text.IndexOf("*/");
        if (length < 0)
        {
            this.urlDiagnostic.Add(new(new(this.line, this.character), new(this.line, this.character + 2)), Hashed.Parser.MissingBlockCommentEnd);

            return this.AddTokenAndSliceWithLineTracking(TokenKind.Invalid, ref text, text.Length);
        }

        length += 2;
        return this.AddTokenAndSliceWithLineTracking(TokenKind.MultiLineComment, ref text, length);
    }

    private bool ReadSingleLineComment(ref ReadOnlySpan<char> span)
    {// // Comment\n
        var idx = Arc.BaseHelper.IndexOfLfOrCrLf(span, out var newLineLength);
        if (idx < 0)
        {
            this.AddTokenAndSlice(TokenKind.SingleLineComment, ref span, span.Length);
            return false;
        }
        else
        {
            this.AddTokenAndSlice(TokenKind.SingleLineComment, ref span, idx);
            this.Slice(ref span, newLineLength);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Diagnostics.Range NewRange(int length)
    {
        return new(new(this.line, this.character), new(this.line, this.character + length));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Slice(ref ReadOnlySpan<char> span, int length)
    {
        span = span.Slice(length);
        this.position += length;
        this.character += length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddToken(Token token)
    {
        this.tokenList.Add(token);
        this.numberOfTokens++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddTokenAndSlice(TokenKind tokenKind, ref ReadOnlySpan<char> span, int length)
    {
        this.tokenList.Add(new(tokenKind, this.text.Slice(this.position, length)));
        this.numberOfTokens++;

        span = span.Slice(length);
        this.position += length;
        this.character += length;
    }

    private int AddTokenAndSliceWithLineTracking(TokenKind tokenKind, ref ReadOnlySpan<char> span, int length)
    {
        this.tokenList.Add(new(tokenKind, this.text.Slice(this.position, length)));
        this.numberOfTokens++;

        var consumed = span.Slice(0, length);
        var lastLf = consumed.LastIndexOf(Constants.LfChar);
        var lineFeeds = 0;
        if (lastLf >= 0)
        {
            lineFeeds = consumed.Count(Constants.LfChar);
            this.line += lineFeeds;
            this.character = consumed.Length - lastLf - 1;
        }
        else
        {
            this.character += length;
        }

        this.position += length;
        span = span.Slice(length);
        return lineFeeds;
    }

    private void ClearToken()
    {
        this.numberOfTokens = 0;
        this.tokenList.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NextLine(int lineFeeds = 1)
    {
        this.line += lineFeeds;
        this.character = 0;
    }
}
