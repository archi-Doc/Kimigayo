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

internal enum ReaderMode : byte
{
    StartOfLine,
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

internal static class ReaderHelper
{
    public static bool IsLineContext(this ReaderMode readerContext) => readerContext switch
    {
        ReaderMode.StartOfLine => true,
        _ => false,
    };
}

internal class Reader
{
    #region FieldAndProperty

    public ReaderMode CurrentMode { get; private set; }

    #endregion

    public Reader(KimiControl kimiControl, ReadOnlySpan<char> text)
    {
        this.span = text;
    }

    public bool Read(out TokenKind token)
    {
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
