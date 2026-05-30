// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

internal enum ReaderMode : byte
{
    Global,
}

internal enum ReaderToken : byte
{
    Keyword,
    Identifier,
    Indent,
    Trivia,
    Eof,
}

internal static class ReaderHelper
{
    public static bool IsLineContext(this ReaderMode readerContext) => readerContext switch
    {
        ReaderMode.Global => true,
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

    public void Read(out ReaderToken token, out ReadOnlySpan<char> text)
    {
        var numberOfSpaces = Arc.BaseHelper.CountLeadingSpaces(this.span);
        if (numberOfSpaces > 0)
        {// Space
            if (this.CurrentMode.IsLineContext())
            {


            }
            else
            {
                this.span = this.span.Slice(numberOfSpaces);
            }

        }

        token = ReaderToken.Keyword;
        text = default;
    }
}
