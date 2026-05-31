// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

internal readonly struct Token
{
    public readonly TokenKind Kind;

    public readonly Memory<char> Text;

    public Token(TokenKind kind, Memory<char> span)
    {
        this.Kind = kind;
        this.Text = span;
    }
}

internal enum TokenKind : byte
{
    Eof,
    Trivia,
    Indent,
    Keyword,
    Attribute,
    Identifier,
    Assignment,
    Reference,
}

internal enum LineFeedKind : byte
{
    Scope, // Indent
    Parenthesis, // (,)
}

internal static class ReaderHelper
{
}

internal class Reader
{
    #region FieldAndProperty

    private Stack<LineFeedKind> lineFeedStack = new();
    private List<Token> tokenList = new();
    private int numberOfTokens;

    #endregion

    public Reader(KimiControl kimiControl)
    {
    }

    public List<Token> Read(ref ReadOnlySpan<char> span)
    {
Entry:
        this.tokenList.Clear();

        // Skip spaces
        var numberOfSpaces = Arc.BaseHelper.CountLeadingSpaces(span);
        span = span.Slice(numberOfSpaces);
        if (span.Length == 0)
        {
            return [];
        }
        else if (span[0] == Constants.LfChar)
        {// Empty sentence (\n)
            goto Entry;
        }
        else if (span.Length >= 2 &&
            span[0] == Constants.CrChar &&
            span[1] == Constants.LfChar)
        {// Empty sentence (\r\n)
            goto Entry;
        }

        var remainingSpaces = numberOfSpaces % Constants.IndentationSpaces;
        if (remainingSpaces > 0)
        {// Invalid indentation
            numberOfSpaces += Constants.IndentationSpaces - remainingSpaces;
        }

        var numberOfIndents = numberOfSpaces / Constants.IndentationSpaces;

        var previousIndents = this.tokenList.Count;

        this.tokenList[1] = default;
        token = default;

        if (this.CurrentMode == ReaderMode.StartOfLine)
        {

        }
        else
        {
            while (this.span.Length > 0 && this.span[0] == ' ')
            {
                this.span = this.span.Slice(1);
            }

            this.span = this.span.Slice(numberOfSpaces);
        }

        token = TokenKind.Keyword;
        text = default;
    }

    private void AddToken(Token token)
    {
        if (this.numberOfTokens >= this.tokenList.Count)
        {
            this.tokenList.EnsureCapacity(this.numberOfTokens + 1);
        }

        this.tokenList[this.numberOfTokens++] = token;
    }

    private bool Read_StartOfLine(out TokenKind token)
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
    }
}
