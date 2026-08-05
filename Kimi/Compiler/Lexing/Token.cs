// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public readonly partial struct Token
{// 1 + 1 + 8 + 16 -> 32
    public static readonly Token Invalid = default;

    public readonly TokenKind Kind;

    public readonly bool IsMissing;

    public readonly int Start;

    public readonly int Length;

    public readonly SourceRange Range;

    public bool IsValid => this.Kind != TokenKind.Invalid;

    public Token(TokenKind kind, int start, int length, SourceRange range)
    {
        this.Kind = kind;
        this.Start = start;
        this.Length = length;
        this.Range = range;
    }

    public Token(TokenKind kind, int start, int length, int line, int character)
    {
        this.Kind = kind;
        this.Start = start;
        this.Length = length;
        this.Range = new(new(line, character), new(line, character + length));
    }

    public Token(TokenKind kind, bool isMissing = false)
    {
        this.Kind = kind;
        this.IsMissing = isMissing;
    }

    public Token(TokenKind kind, SourceRange range)
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
