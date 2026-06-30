// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

[TinyhandObject]
public readonly partial struct Token
{// 1 + 1 + 16 + 16 -> 40
    public static readonly Token Invalid = default;

    [Key(0)]
    public readonly TokenKind Kind;

    [Key(1)]
    public readonly bool IsMissing;

    [Key(2)]
    public readonly ReadOnlyMemory<char> Text;

    public readonly SourceRange Range;

    public ReadOnlySpan<char> Span => this.Text.Span;

    public int Length => this.Text.Length;

    public Token(TokenKind kind, ReadOnlyMemory<char> span, Diagnostics.SourceRange range)
    {
        this.Kind = kind;
        this.Text = span;
        this.Range = range;
    }

    public Token(TokenKind kind, ReadOnlyMemory<char> span, int line, int character)
    {
        this.Kind = kind;
        this.Text = span;
        this.Range = new(new(line, character), new(line, character + span.Length));
    }

    public Token(TokenKind kind, bool isMissing = false)
    {
        this.Kind = kind;
        this.IsMissing = isMissing;
    }

    public override string ToString()
    {
        if (this.Kind == TokenKind.Identifier ||
            this.Kind == TokenKind.NumericLiteral ||
            this.Kind == TokenKind.StringLiteral ||
            this.Kind == TokenKind.RawStringLiteral ||
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
