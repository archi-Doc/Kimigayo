// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

/// <summary>
/// Represents a lexical token produced by the lexer.
/// </summary>
public readonly record struct Token
{// 1 + 1 + 8 -> 12
    public static readonly Token Invalid = default;

    public readonly TokenKind Kind; // 1

    public readonly bool IsMissing; // 1

    public readonly SourceSpan Span; // 8

    public int Start => this.Span.Start;

    public int Length => this.Span.Length;

    public bool IsValid => this.Kind != TokenKind.Invalid;

    public Token(TokenKind kind, int start, int length)
    {
        this.Kind = kind;
        this.Span = new(start, length);
    }

    public Token(TokenKind kind, bool isMissing = false)
    {
        this.Kind = kind;
        this.IsMissing = isMissing;
    }

    public Token(TokenKind kind, SourceSpan range)
    {
        this.Kind = kind;
        this.Span = range;
    }

    public override string ToString()
    {
        return $"({this.Kind.ToString()})";
    }
}
