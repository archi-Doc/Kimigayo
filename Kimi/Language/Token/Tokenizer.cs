// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
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
    }

    public (List<Token> List, int Count) Read()
    {
        this.tokenList.Clear();
        this.numberOfTokens = 0;
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
        else if (span.Length >= 2 &&
            span[0] == Constants.CrChar &&
            span[1] == Constants.LfChar)
        {// Empty line (\r\n)
            this.Slice(ref span, 2);
            this.NextLine();
            goto Loop;
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
            else if (span.Length == 1)
            {// Single character
                if (span[0] == Constants.CloseParenthesisChar)
                {// )
                    this.AddTokenAndSlice(TokenKind.CloseParenthesis, ref span, 1);
                    this.numberOfBrackets--;
                }
                else if (span[0] == Constants.CloseBracketChar)
                {// ]
                    this.AddTokenAndSlice(TokenKind.CloseBracket, ref span, 1);
                    this.numberOfBrackets--;
                }
                else if (span[0] == Constants.CloseBraceChar)
                {// }
                    this.AddTokenAndSlice(TokenKind.CloseBrace, ref span, 1);
                    this.numberOfBrackets--;
                }
                else if (span[0] == Constants.LfChar)
                {// \n
                    this.Slice(ref span, 1);
                }
                else
                {// Invalid token
                    this.urlDiagnostic.Add(this.NewRange(1), Hashed.Parser.InvalidCharacterAtEndOfFile);
                    this.Slice(ref span, 1);
                }

                break;
            }

            // span.Length >= 2
            switch (span[0])
            {
                case Constants.AttributeChar:
                    {// #Attribute()
                        break;
                    }

                case Constants.CrChar:
                    if (span[1] == Constants.LfChar)
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
                    if (span[1] == Constants.AmpersandChar)
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
                    if (span[1] == Constants.EqualsChar)
                    {// *=
                        this.AddTokenAndSlice(TokenKind.AsteriskEquals, ref span, 2);
                    }
                    else
                    {// *
                        this.AddTokenAndSlice(TokenKind.Asterisk, ref span, 1);
                    }

                    break;

                case Constants.BarChar: // | || |=
                    if (span[1] == Constants.BarChar)
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
                    if (span[1] == Constants.EqualsChar)
                    {// ^=
                        this.AddTokenAndSlice(TokenKind.CaretEquals, ref span, 2);
                    }
                    else
                    {// ^
                        this.AddTokenAndSlice(TokenKind.Caret, ref span, 1);
                    }

                    break;

                case Constants.EqualsChar: // = == =>
                    if (span[1] == Constants.EqualsChar)
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
                    if (span[1] == Constants.EqualsChar)
                    {// !=
                        this.AddTokenAndSlice(TokenKind.ExclamationEquals, ref span, 2);
                    }
                    else
                    {// !
                        this.AddTokenAndSlice(TokenKind.Exclamation, ref span, 1);
                    }

                    break;

                case Constants.GreaterThanChar: // > >= >>=
                    if (span[1] == Constants.EqualsChar)
                    {// >=
                        this.AddTokenAndSlice(TokenKind.GreaterThanEquals, ref span, 2);
                    }
                    else if (span[1] == Constants.GreaterThanChar &&
                        span.Length >= 3 && span[2] == Constants.EqualsChar)
                    {// >>=
                        this.AddTokenAndSlice(TokenKind.GreaterThanGreaterThanEquals, ref span, 3);
                    }
                    else
                    {// >
                        this.AddTokenAndSlice(TokenKind.GreaterThan, ref span, 1);
                    }

                    break;

                case Constants.LessThanChar: // < <= <<=
                    if (span[1] == Constants.EqualsChar)
                    {// <=
                        this.AddTokenAndSlice(TokenKind.LessThanEquals, ref span, 2);
                    }
                    else if (span[1] == Constants.LessThanChar &&
                        span.Length >= 3 && span[2] == Constants.EqualsChar)
                    {// <<=
                        this.AddTokenAndSlice(TokenKind.LessThanLessThanEquals, ref span, 3);
                    }
                    else
                    {// <
                        this.AddTokenAndSlice(TokenKind.LessThan, ref span, 1);
                    }

                    break;

                case Constants.MinusChar: // -- -= -
                    if (span[1] == Constants.MinusChar)
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
                    if (span[1] == Constants.EqualsChar)
                    {// %=
                        this.AddTokenAndSlice(TokenKind.PercentEquals, ref span, 2);
                    }
                    else
                    {// %
                        this.AddTokenAndSlice(TokenKind.Percent, ref span, 1);
                    }

                    break;

                case Constants.PlusChar: // ++ += +
                    if (span[1] == Constants.PlusChar)
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
                    if (span[1] == Constants.SlashChar)
                    {// //
                        this.ReadSingleLineComment(ref span);
                        this.NextLine();
                        goto NextLine;
                    }
                    else if (span[1] == Constants.AsteriskChar)
                    {// /*
                        var lineFeeds = this.ReadMultiLineComment(ref span);
                        this.NextLine(lineFeeds);
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
                            this.AddTokenAndSlice(tokenKind, ref span, 1);
                            this.numberOfBrackets += depth;
                        }
                        else if (LanguageHelper.IsDecimalNumberStart(span))
                        {// Numeric literal
                         // If the current position starts a numeric literal, scan the entire numeric literal before checking separators.
                            var length = LanguageHelper.ScanDecimalNumber(span);
                            this.AddTokenAndSlice(TokenKind.NumericLiteral, ref span, length);
                        }
                        else if (LanguageHelper.TryGetStringLiteralLength(span, out var literalLength))
                        {// String literal
                            this.AddTokenAndSlice(TokenKind.Literal, ref span, literalLength);
                        }
                        else
                        {// Keyword or Identifier
                            var length = LanguageHelper.IndexOfSeparator(span);
                            if (length < 0)
                            {
                                length = span.Length;
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

    private int ReadMultiLineComment(ref ReadOnlySpan<char> span)
    {// /* Comment */
        var idx = span.IndexOf("*/", StringComparison.Ordinal);
        if (idx < 0)
        {
            idx = span.Length;
            this.urlDiagnostic.Add(new(new(this.line, this.position), new(this.line, this.position + 2)), Hashed.Parser.MissingBlockCommentEnd);
        }
        else
        {
            idx += 2;
        }

        var lineFeeds = span.Slice(0, idx).Count(Constants.LfChar);
        this.AddTokenAndSlice(TokenKind.MultiLineComment, ref span, idx);
        return lineFeeds;
    }

    private void ReadSingleLineComment(ref ReadOnlySpan<char> span)
    {// // Comment\n
        var idx = Arc.BaseHelper.IndexOfLfOrCrLf(span, out var newLineLength);
        if (idx < 0)
        {
            this.AddTokenAndSlice(TokenKind.SingleLineComment, ref span, span.Length);
        }
        else
        {
            this.AddTokenAndSlice(TokenKind.SingleLineComment, ref span, idx);
            this.Slice(ref span, newLineLength);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Diagnostics.Range NewRange(int length)
    {
        return new(new(this.line, this.position), new(this.line, this.position + length));
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

    /*private bool Read_StartOfLine(out TokenKind token)
    {
        var numberOfSpaces = Arc.BaseHelper.CountLeadingSpaces(this.span);
        this.span = this.span.Slice(numberOfSpaces);

        if (numberOfSpaces > 0)
        {// Spaces
            var remainingSpaces = numberOfSpaces % Constants.IndentationSpaces;
            if (remainingSpaces > 0)
            {// Invalid indentation
                numberOfSpaces += Constants.IndentationSpaces - remainingSpaces;
            }

            var numberOfIndents = numberOfSpaces / Constants.IndentationSpaces;
            token = TokenKind.Indent;
            token = new(TokenKind.Indent, )
            return true;
        }
        else
        {// Keyword: namespace, public
            var idx = this.span.IndexOf(Constants.SpaceChar);
        }
    }*/

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NextLine(int lineFeeds = 1)
    {
        this.line += lineFeeds;
        this.character = 0;
    }
}
