// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Kimigayo.Diagnostics;
using Kimigayo.Language;

namespace Kimigayo.Language;

public ref struct TokenReader
{
    public const int MaxDepth = 10;

    #region FieldAndProperty

    public readonly DiagnosticCollection Diagnostic;

    public readonly CodeContext CodeContext;

    private readonly IReadOnlyList<Token> list;

    public int Position { get; private set; }

    public int Depth { get; private set; }

    public int Count => this.list.Count;

    public int Remaining => this.list.Count - this.Position;

    public bool IsEmpty => this.Position >= this.list.Count;

    public TokenKind CurrentTokenKind => this.Position < this.list.Count ? this.list[this.Position].Kind : TokenKind.Invalid;

    #endregion

    public TokenReader(DiagnosticCollection diagnostic, IReadOnlyList<Token> tokens, CodeContext codeContext)
    {
        this.Diagnostic = diagnostic;
        this.CodeContext = codeContext;
        this.list = tokens;
    }

    /// <summary>
    /// Skips comment tokens and returns whether a non-comment token remains.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if a non-comment token remains after skipping comments;
    /// otherwise, <see langword="false"/> if the end of the token list was reached.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool SkipCommentsAndHasMore()
    {
        while (this.Position < this.Count)
        {
            var kind = this.list[this.Position].Kind;
            if (kind == TokenKind.SingleLineComment ||
                kind == TokenKind.MultiLineComment)
            {
                this.Position++;
                continue;
            }

            return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncrementDepth()
    {
        if (this.Depth++ > MaxDepth)
        {
            throw new InvalidOperationException("The token depth has reached the maximum limit");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DecrementDepth()
    {
        this.Depth--;
        Debug.Assert(this.Depth >= 0);
    }

    public bool TryRead(out Token token)
    {
        if (this.SkipCommentsAndHasMore())
        {
            token = this.list[this.Position++];
            return true;
        }

        token = default;
        return false;
    }

    public bool TryPeek(out Token token)
    {
        if (this.SkipCommentsAndHasMore())
        {
            token = this.list[this.Position];
            return true;
        }

        token = default;
        return false;
    }

    public bool TryConsume(TokenKind targetKind, out SourceRange range, bool addDiagnostic = true)
    {
        if (this.SkipCommentsAndHasMore())
        {
            if (this.list[this.Position].Kind == targetKind)
            {
                range = this.list[this.Position].Range;
                this.Position++;
                return true;
            }
            else
            {
                if (addDiagnostic)
                {
                    this.Diagnostic.AddToken(this.list[this.Position], Hashed.Kimi.TokenMismatch, targetKind.ToText());
                }

                range = default;
                return false;
            }
        }

        if (addDiagnostic)
        {
            this.Diagnostic.AddToken(this.list[this.Position], Hashed.Kimi.TokenMismatch, targetKind.ToText());
        }

        range = default;
        return false;
    }

    public ReadOnlySpan<char> ReadIdentifier()
    {
        if (this.SkipCommentsAndHasMore() &&
            this.list[this.Position].Kind == TokenKind.Identifier)
        {
            var identifier = this.list[this.Position].Text.Span;
            this.Position++;
            return identifier;
        }

        return [];
    }

    public bool TryConsumeIdentifier(ReadOnlySpan<char> name)
    {
        if (this.SkipCommentsAndHasMore())
        {
            if (this.list[this.Position].Kind == TokenKind.Identifier &&
                this.list[this.Position].Text.Span.Equals(name, StringComparison.Ordinal))
            {
                this.Position++;
                return true;
            }
        }

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

    public SourceRange CurrentRange()
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
