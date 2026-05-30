// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

internal readonly ref struct ReaderToken
{
    public readonly ReaderTokenKind Kind;

    public readonly ReadOnlySpan<char> Span;

    public readonly int NumberOfIndents;

    public ReaderToken(ReaderTokenKind kind, ReadOnlySpan<char> span, int numberOfIndents = 0)
    {
        this.Kind = kind;
        this.Span = span;
        this.NumberOfIndents = numberOfIndents;
    }
}

internal enum ReaderMode : byte
{
    StartOfLine,
}

internal enum ReaderTokenKind : byte
{
    Eof,
    Indent,
    Keyword,
    Attribute,
    Identifier,
    Trivia,
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

internal ref struct Reader
{
    #region FieldAndProperty

    private ReadOnlySpan<char> span;

    public ReaderMode CurrentMode { get; private set; }

    #endregion

    public Reader(KimiControl kimiControl, ReadOnlySpan<char> text)
    {
        this.span = text;
    }

    public bool Read(out ReaderTokenKind token)
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

        token = ReaderTokenKind.Keyword;
        text = default;
    }

    private bool Read_StartOfLine(out ReaderTokenKind token)
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
            token = ReaderTokenKind.Indent;
            token = new(ReaderTokenKind.Indent, )
            return true;
        }
        else
        {// Keyword: namespace, public
            var idx = this.span.IndexOf(Constants.SpaceChar);
        }
    }
}
