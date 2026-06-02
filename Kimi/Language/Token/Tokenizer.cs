// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

internal class Tokenizer
{
    #region FieldAndProperty

    private readonly KimiControl kimiControl;
    private readonly UrlDiagnostic urlDiagnostic;

    private ReadOnlyMemory<char> text;
    private int position;
    private int line;
    private int character;

    private int previousIndents;
    private bool requiresIndent;

    // private Stack<LineFeedKind> lineFeedStack = new();
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

        this.previousIndents = -1;
        this.requiresIndent = false;
    }

    public (List<Token> List, int Count) Read()
    {
Entry:
        this.numberOfTokens = 0;
        var span = this.text.Slice(this.position).Span;
        if (span.Length == 0)
        {// Eof
            return (this.tokenList, this.numberOfTokens);
        }

        if (this.previousIndents >= 0)
        {// The spaces/indentation has already been processed in the previous loop.
            while (span.Length > 0)
            {
                while (span[0] == Constants.SpaceChar)
                {// Skip spaces
                    span = span.Slice(1);
                    if (span.Length == 0)
                    {// Eof
                        break;
                    }
                }

                if (span.Length == 0)
                {// Eof
                    break;
                }
                else if (span.Length == 1)
                {// Single character
                    if (span[0] == Constants.CloseParenthesisChar)
                    {// )
                        this.AddToken(new(TokenKind.CloseParenthesis, this.text.Slice(this.position, 1)));
                        this.Slice(ref span, 1);
                    }
                    else if (span[0] == Constants.CloseBracketChar)
                    {// ]
                        this.AddToken(new(TokenKind.CloseBracket, this.text.Slice(this.position, 1)));
                        this.Slice(ref span, 1);
                    }
                    else if (span[0] == Constants.CloseBraceChar)
                    {// }
                        this.AddToken(new(TokenKind.CloseBrace, this.text.Slice(this.position, 1)));
                        this.Slice(ref span, 1);
                    }
                    else if (span[0] == Constants.LfChar)
                    {// \n
                        this.Slice(ref span, 1);
                        break;
                    }
                    else
                    {// Invalid token
                        this.urlDiagnostic.Add(this.NewRange(1), Hashed.Parser.InvalidCharacterAtEndOfFile);
                        this.Slice(ref span, 1);
                    }

                    break;
                }

                // span.Length >= 2
                if (span[0] == Constants.AttributeChar)
                {// #Attribute()
                }
                else if (span[0] == Constants.CrChar && span[1] == Constants.LfChar)
                {// \r\n
                    this.Slice(ref span, 2);
                    this.NextLine();
                    break;
                }
                else if (LanguageHelper.GetSingleCharTokenKind(span[0]) is TokenKind tokenKind &&
                    tokenKind != TokenKind.None)
                {
                    this.AddTokenAndSlice(tokenKind, ref span, 1);
                }
                else if (span[0] == Constants.AmpersandChar)
                {// && &= &
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
                }
                else if (span[0] == Constants.AsteriskChar)
                {// * *=
                    if (span[1] == Constants.EqualsChar)
                    {// *=
                        this.AddTokenAndSlice(TokenKind.AsteriskEquals, ref span, 2);
                    }
                    else
                    {// *
                        this.AddTokenAndSlice(TokenKind.Asterisk, ref span, 1);
                    }
                }
                else if (span[0] == Constants.BarChar)
                {// | || |=
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
                }
                else if (span[0] == Constants.SlashChar)
                {// // /* /= /
                    if (span[1] == Constants.SlashChar)
                    {// "//"
                        this.ReadSingleLineComment(ref span);
                        this.NextLine();
                        break; // NextLine
                    }
                    else if (span[1] == '*')
                    {// "/*"
                        var lineFeeds = this.ReadMultiLineComment(ref span);
                        this.NextLine(lineFeeds);
                    }
                    else if (span[1] == '=')
                    {// "/="
                        this.AddTokenAndSlice(TokenKind.SlashEquals, ref span, 2);
                    }
                    else
                    {// /
                        this.AddTokenAndSlice(TokenKind.Slash, ref span, 1);
                    }
                }
                else if (span[0] == '*')
                {// * *=
                    if (span[1] == Constants.EqualsChar)
                    {// *=
                        this.AddTokenAndSlice(TokenKind.AsteriskEquals, ref span, 2);
                    }
                    else
                    {// *
                        this.AddTokenAndSlice(TokenKind.Asterisk, ref span, 1);
                    }
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
                        if (tokenKind2 == TokenKind.Group)
                        {
                            this.requiresIndent = true;
                        }

                        this.AddTokenAndSlice(tokenKind2, ref span, length);
                    }
                    else
                    {// Identifier
                        this.AddTokenAndSlice(TokenKind.Identifier, ref span, length);
                    }
                }
            }
        }

        if (span.Length == 0)
        {// Eof
            return (this.tokenList, this.numberOfTokens);
        }

        // Skip spaces
        var numberOfSpaces = Arc.BaseHelper.CountLeadingSpaces(span);
        this.Slice(ref span, numberOfSpaces);

        if (span[0] == Constants.LfChar)
        {// Empty line (\n)
            this.Slice(ref span, 1);
            this.NextLine();
            goto Entry;
        }
        else if (span.Length >= 2 &&
            span[0] == Constants.CrChar &&
            span[1] == Constants.LfChar)
        {// Empty line (\r\n)
            this.Slice(ref span, 2);
            this.NextLine();
            goto Entry;
        }

        var unnecessarySpaces = numberOfSpaces % Constants.IndentationSpaces;
        if (unnecessarySpaces > 0)
        {// Invalid indentation
            numberOfSpaces += Constants.IndentationSpaces - unnecessarySpaces;
            this.urlDiagnostic.Add(new(new(this.line, 0), new(this.line, this.character)), Hashed.Parser.InvalidIndentation, Constants.IndentationSpaces);
        }

        var numberOfIndents = numberOfSpaces / Constants.IndentationSpaces;

        if (numberOfIndents == this.previousIndents ||
            this.previousIndents < 0)
        {// Same indent or initial state
        }
        else if (numberOfIndents < this.previousIndents)
        {// -Indent
        }
        else
        {// +Indent
        }

        this.previousIndents = numberOfIndents;
    }

    private int ReadMultiLineComment(ref ReadOnlySpan<char> span)
    {// "/* Comment */"
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
    {// "// Comment\n"
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
