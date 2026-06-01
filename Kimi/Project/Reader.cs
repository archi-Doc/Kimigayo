// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimigayo.Diagnostics;
using Kimigayo.Language;

namespace Kimigayo;

internal readonly struct Token
{
    public readonly TokenKind Kind;

    public readonly ReadOnlyMemory<char> Text;

    public Token(TokenKind kind, ReadOnlyMemory<char> span)
    {
        this.Kind = kind;
        this.Text = span;
    }
}

/*internal enum LineFeedKind : byte
{
    Scope, // Indent
    Parenthesis, // (,)
}*/

internal class Reader
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

    public Reader(KimiControl kimiControl, UrlDiagnostic urlDiagnostic)
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
            }

            if (span[0] == Constants.AttributeChar)
            {// #Attribute()
            }
            else if (span[0] == '/')
            {// "//" "/*" "/", "/="
            }
        }

        // Separator Space, (, ), Cr, Lf, =, <, >, +, -, %, &, |, ','
        span.IndexOfAny("ABC");

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
            this.urlDiagnostic.Add(new(new(this.line, 0), new(this.line, this.character)), Hashed.Reader.InvalidIndent);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Slice(ref ReadOnlySpan<char> span, int start)
    {
        span = span.Slice(start);
        this.position += start;
        this.character += start;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddToken(Token token)
    {
        if (this.numberOfTokens >= this.tokenList.Count)
        {
            this.tokenList.EnsureCapacity(this.numberOfTokens + 1);
        }

        this.tokenList[this.numberOfTokens++] = token;
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
    private void NextLine()
    {
        this.line++;
        this.character = 0;
    }
}
