// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

/// <summary>
/// Represents a lexical token produced by the lexer.
/// </summary>
public readonly record struct Token
{
    // Keep the payload compact because token sequences can be large.

    /// <summary>
    /// Represents an invalid or unavailable token.
    /// </summary>
    public static readonly Token Invalid = default;

    /// <summary>
    /// The token kind.
    /// </summary>
    public readonly TokenKind Kind; // 1

    /// <summary>
    /// Indicates whether the parser synthesized the token.
    /// </summary>
    public readonly bool IsMissing; // 1

    /// <summary>
    /// The token span in the source document.
    /// </summary>
    public readonly SourceSpan Span; // 8

    /// <summary>
    /// Gets the token's absolute source offset.
    /// </summary>
    public int Start => this.Span.Start;

    /// <summary>
    /// Gets the token length in characters.
    /// </summary>
    public int Length => this.Span.Length;

    /// <summary>
    /// Gets a value indicating whether the token has a valid kind.
    /// </summary>
    public bool IsValid => this.Kind != TokenKind.Invalid;

    /// <summary>
    /// Initializes a new instance of the <see cref="Token"/> struct.
    /// </summary>
    /// <param name="kind">The token kind.</param>
    /// <param name="start">The absolute source offset.</param>
    /// <param name="length">The token length in characters.</param>
    public Token(TokenKind kind, int start, int length)
    {
        this.Kind = kind;
        this.Span = new(start, length);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Token"/> struct without a source span.
    /// </summary>
    /// <param name="kind">The token kind.</param>
    /// <param name="isMissing">Whether the parser synthesized the token.</param>
    public Token(TokenKind kind, bool isMissing = false)
    {
        this.Kind = kind;
        this.IsMissing = isMissing;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Token"/> struct.
    /// </summary>
    /// <param name="kind">The token kind.</param>
    /// <param name="range">The token span in the source document.</param>
    public Token(TokenKind kind, SourceSpan range)
    {
        this.Kind = kind;
        this.Span = range;
    }

    /// <summary>
    /// Returns the token kind as a display string.
    /// </summary>
    /// <returns>The token kind enclosed in parentheses.</returns>
    public override string ToString()
    {
        return $"({this.Kind.ToString()})";
    }
}
