// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Language;

namespace Kimi.Language;

public ref struct TokenReader
{
    #region FieldAndProperty

    private readonly IReadOnlyList<Token> list;

    public int Position { get; private set; }

    public int Count => this.list.Count;

    public bool IsEmpty => this.list.Count == 0;

    #endregion

    public TokenReader(IReadOnlyList<Token> tokens)
    {
        this.list = tokens;
    }

    public bool TryRead(out Token token)
    {
        while (this.Position < this.Count)
        {
            if (this.list[this.Position].Kind == TokenKind.SingleLineComment ||
                this.list[this.Position].Kind == TokenKind.MultiLineComment)
            {
                this.Position++;
                continue;
            }

            token = this.list[this.Position++];
            return true;
        }

        token = default;
        return false;
    }

    public bool TryPeek(out Token token)
    {
        while (this.Position < this.Count)
        {
            if (this.list[this.Position].Kind == TokenKind.SingleLineComment ||
                this.list[this.Position].Kind == TokenKind.MultiLineComment)
            {
                this.Position++;
                continue;
            }

            token = this.list[this.Position];
            return true;
        }

        token = default;
        return false;
    }

    public bool MoveNext()
    {
        while (this.Position < this.Count)
        {
            if (this.list[this.Position].Kind == TokenKind.SingleLineComment ||
                this.list[this.Position].Kind == TokenKind.MultiLineComment)
            {
                this.Position++;
                continue;
            }

            this.Position++;
            return true;
        }

        return false;
    }

    public Kimigayo.Diagnostics.SourceRange CurrentRange()
    {
        if (this.Position < this.Count)
        {
            return this.list[this.Position].Range;
        }
        else if (this.Position > 0)
        {
            var range = this.list[this.Position - 1].Range;
            return new(range.End, range.End);
        }
        else
        {
            return default;
        }
    }
}
