// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

public readonly struct Token
{
    public readonly TokenKind Kind;

    public readonly bool IsMissing;

    public readonly ReadOnlyMemory<char> Text;

    public readonly int Line;

    public readonly int Character;

    public ReadOnlySpan<char> Span => this.Text.Span;

    public int Length => this.Text.Length;

    public Token(TokenKind kind, ReadOnlyMemory<char> span, bool isMissing = false)
    {
        this.Kind = kind;
        this.Text = span;
        this.IsMissing = isMissing;
    }

    public Token(TokenKind kind, ReadOnlyMemory<char> span, int line, int character)
    {
        this.Kind = kind;
        this.Text = span;
        this.Line = line;
        this.Character = character;
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
