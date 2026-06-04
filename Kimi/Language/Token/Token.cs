// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Token;

internal readonly struct Token
{
    public readonly TokenKind Kind;

    public readonly ReadOnlyMemory<char> Text;

    public Token(TokenKind kind, ReadOnlyMemory<char> span)
    {
        this.Kind = kind;
        this.Text = span;
    }

    public override string ToString()
    {
        if (this.Kind == TokenKind.Identifier ||
            this.Kind == TokenKind.NumericLiteral ||
            this.Kind == TokenKind.Literal ||
            this.Kind == TokenKind.RawLiteral ||
            this.Kind == TokenKind.SingleLineComment ||
            this.Kind == TokenKind.MultiLineComment)
        {
            return $"({this.Kind.ToString()}:'{this.Text}')";
        }
        else
        {
            return $"({this.Kind.ToString()})";
        }
    }
}
