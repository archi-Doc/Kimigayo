// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

/// <summary>
/// Represents a lexical token produced by the lexer.
/// </summary>
public readonly partial struct Token
{// 1 + 1 + 8 -> 12
    public static readonly Token Invalid = default;

    public readonly TokenKind Kind; // 1

    public readonly bool IsMissing; // 1

    public readonly TextSpan Range; // 8

    public int Start => this.Range.Start;

    public int Length => this.Range.Length;

    public bool IsValid => this.Kind != TokenKind.Invalid;

    public Token(TokenKind kind, int start, int length)
    {
        this.Kind = kind;
        this.Range = new(start, length);
    }

    public Token(TokenKind kind, bool isMissing = false)
    {
        this.Kind = kind;
        this.IsMissing = isMissing;
    }

    public Token(TokenKind kind, TextSpan range)
    {
        this.Kind = kind;
        this.Range = range;
    }

    public override string ToString()
    {
        /*if (this.Kind == TokenKind.Identifier ||
            this.Kind == TokenKind.NumericLiteral ||
            this.Kind == TokenKind.StringLiteral ||
            this.Kind == TokenKind.RawStringLiteral ||
            this.Kind == TokenKind.SingleLineComment ||
            this.Kind == TokenKind.MultiLineComment)
        {
            return $"({this.Kind.ToString()}:'{this.Text}')";
        }*/

        return $"({this.Kind.ToString()})";
    }
}
