// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo;

internal enum ReaderContext : byte
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

internal ref struct Reader
{
    #region FieldAndProperty

    private readonly int indentationSpaces = 4;
    private ReadOnlySpan<char> text;

    public ReaderContext Current { get; private set; }

    #endregion

    public Reader(KimiControl kimiControl, ReadOnlySpan<char> text)
    {
        this.text = text;
    }

    public void Read(out ReaderToken token, out ReadOnlySpan<char> text)
    {
        var span = this.text;

        if (char.IsWhiteSpace(span[0]))
        {// Space

        }

        token = ReaderToken.Keyword;
        text = default;
    }
}
