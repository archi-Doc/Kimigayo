// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

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
