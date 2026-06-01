// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimigayo.Diagnostics;

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

    private readonly KimiControl kimiControl;
    private readonly UrlDiagnostic urlDiagnostic;

    private ReadOnlyMemory<char> text;
    private int position;
    private int line;
    private int character;

    // private Stack<LineFeedKind> lineFeedStack = new();
    private List<Token> tokenList = new();
    private int numberOfTokens;

    #endregion

    public Reader(KimiControl kimiControl, UrlDiagnostic urlDiagnostic)
    {
        this.kimiControl = kimiControl;
        this.urlDiagnostic = urlDiagnostic;
    }

    public void Setup(ReadOnlyMemory<char> text, int line, int character)
    {
        this.text = text;
        this.position = 0;
        this.line = line;
        this.character = character;
    }

    public ReadOnlySpan<Token> Read()
    {
        int currentIndents;
        bool requiresIndent;

Entry:
        var span = this.text.Slice(this.position).Span;
        this.tokenList.Clear();

        // Skip spaces
        var numberOfSpaces = Arc.BaseHelper.CountLeadingSpaces(span);
        Slice(ref span, numberOfSpaces);
        if (span.Length == 0)
        {// Eof
            return [];
        }
        else if (span[0] == Constants.LfChar)
        {// Empty line (\n)
            Slice(ref span, 1);
            this.line++;
            goto Entry;
        }
        else if (span.Length >= 2 &&
            span[0] == Constants.CrChar &&
            span[1] == Constants.LfChar)
        {// Empty line (\r\n)
            Slice(ref span, 2);
            this.line++;
            goto Entry;
        }

        var unnecessarySpaces = numberOfSpaces % Constants.IndentationSpaces;
        if (unnecessarySpaces > 0)
        {// Invalid indentation
            numberOfSpaces += Constants.IndentationSpaces - unnecessarySpaces;
            this.urlDiagnostic.Add(new(), Hashed.Reader.InvalidIndent);
        }

        var numberOfIndents = numberOfSpaces / Constants.IndentationSpaces;

        var previousIndents = this.tokenList.Count;

        this.tokenList[1] = default;


        token = TokenKind.Keyword;
        text = default;

        void Slice(ref ReadOnlySpan<char> span, int start)
        {
            span = span.Slice(start);
            this.position += start;
            this.character += start;
        }
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void NextLine()
    {
        this.line++;
        this.character = 0;
    }
}
