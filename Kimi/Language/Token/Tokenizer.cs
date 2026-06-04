// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.ComponentModel.Design;
using System.Runtime.CompilerServices;
using Arc.Collections;
using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

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
                        var lineFeeds = this.ReadMultiLineComment(ref span);
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
                        if (LanguageHelper.TryGetSingleCharTokenKind(span[0], out var tokenKind, out var depth))
                        {// Single char token
                            this.numberOfBrackets += depth;
                            if (this.numberOfBrackets < 0)
                            {
                                this.numberOfBrackets = 0;
                                this.urlDiagnostic.Add(this.NewRange(1), Hashed.Parser.UnmatchedClosingBracket);
                            }

                            this.AddTokenAndSlice(tokenKind, ref span, 1);
                        }
                        else if (LanguageHelper.IsDecimalNumberStart(span))
                        {// Numeric literal
                         // If the current position starts a numeric literal, scan the entire numeric literal before checking separators.
                            var length = LanguageHelper.ScanDecimalNumber(span);
                            this.AddTokenAndSlice(TokenKind.NumericLiteral, ref span, length);
                        }
                        else if (LanguageHelper.TryGetStringLiteralLength(span, out var literalLength, out var quoteCount))
                        {// String literal
                            if (literalLength < 0)
                            {// Invalid literal
                                this.urlDiagnostic.Add(this.NewRange(1), Hashed.Parser.MissingStringLiteralEnd);
                                this.AddTokenAndSlice(TokenKind.Invalid, ref span, 1);
                            }
                            else
                            {
                                this.Slice(ref span, quoteCount);
                                this.AddTokenAndSlice(TokenKind.Literal, ref span, literalLength - quoteCount - quoteCount);
                                this.Slice(ref span, quoteCount);
                            }
                        }
                        else
                        {// Keyword or Identifier
                            var length = LanguageHelper.IndexOfSeparator(span);
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

                            if (LanguageHelper.KeywordToKeywordKind.TryGetValue(span.Slice(0, length), out var tokenKind2))
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
    {// /* Comment */
        var length = text.IndexOf("*/");
        if (length < 0)
        {
            this.urlDiagnostic.Add(new(new(this.line, this.character), new(this.line, this.character + 2)), Hashed.Parser.MissingBlockCommentEnd);
            this.Slice(ref text, 2);
            return 0;
        }

        length += 2;
        var span = text.Slice(0, length);
        var idx = span.LastIndexOf(Constants.LfChar);
        if (idx < 0)
        {// Single-line comment
            this.AddTokenAndSlice(TokenKind.MultiLineComment, ref text, length);
            return 0;
        }
        else
        {// Multi-line comment
            var lineFeeds = span.Count(Constants.LfChar);
            this.AddTokenAndSlice(TokenKind.MultiLineComment, ref text, length);
            this.line += lineFeeds;
            this.character = length - idx;
            return lineFeeds;
        }
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

public static class LanguageHelper
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

    static LanguageHelper()
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
            if (i + 1 < text.Length && text[i + 1] == '.')
            {// 1..
                return i;
            }

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

    public static bool TryGetStringLiteralLength(ReadOnlySpan<char> text, out int length, out int quoteCount)
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

    public static bool RequiresImplicitIndentation(TokenKind tokenKind)
        => tokenKind >= TokenKind.Group && tokenKind <= TokenKind.Case;

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
