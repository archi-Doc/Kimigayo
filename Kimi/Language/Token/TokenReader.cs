// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Kimigayo.Diagnostics;

namespace Kimigayo.Language;

public ref struct TokenReader
{
    public const int MaxDepth = 10;

    #region FieldAndProperty

    public readonly DiagnosticCollection Diagnostic;

    public readonly CodeContext CodeContext;

    private readonly ReadOnlySequence<Token> sequence;
    private readonly int count;

    private SequencePosition nextSegmentPosition;
    private ReadOnlySpan<Token> currentSpan;
    private int currentSpanIndex;

    public Token PreviousToken { get; private set; }

    public int Position { get; private set; }

    public int Depth { get; private set; }

    public readonly int Count => this.count;

    public readonly int Remaining => this.count - this.Position;

    public readonly bool IsEmpty => this.Position >= this.count;

    public TokenKind CurrentTokenKind
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return this.TryGetCurrentToken(out var token) ? token.Kind : TokenKind.Invalid;
        }
    }

    #endregion

    public TokenReader(DiagnosticCollection diagnostic, CodeContext codeContext, ReadOnlySequence<Token> tokenSequence)
    {
        this.Diagnostic = diagnostic;
        this.CodeContext = codeContext;

        this.sequence = tokenSequence;
        this.count = checked((int)tokenSequence.Length);

        this.Position = 0;
        this.Depth = 0;

        this.nextSegmentPosition = tokenSequence.Start;
        this.currentSpan = default;
        this.currentSpanIndex = 0;

        this.PreviousToken = default;

        this.MoveToNextNonEmptySpan();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IncrementDepth()
    {
        if (this.Depth >= MaxDepth)
        {
            throw new InvalidOperationException("The token depth has reached the maximum limit");
        }

        this.Depth++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DecrementDepth()
    {
        this.Depth--;
        Debug.Assert(this.Depth >= 0);
    }

    public bool TryRead(out Token token)
    {
        if (this.TryGetCurrentToken(out token))
        {
            this.AdvanceOne();
            return true;
        }

        token = default;
        return false;
    }

    public bool TryPeek(out Token token)
    {
        if (this.TryGetCurrentToken(out token))
        {
            return true;
        }

        token = default;
        return false;
    }

    public bool TryConsume(TokenKind targetKind, out SourceRange range, bool addDiagnostic = true)
    {
        if (this.TryGetCurrentToken(out var token))
        {
            if (token.Kind == targetKind)
            {
                range = token.Range;
                this.AdvanceOne();
                return true;
            }

            if (addDiagnostic)
            {
                this.Diagnostic.AddToken(token, Hashed.Kimi.TokenMismatch, targetKind.ToText());
            }

            range = default;
            return false;
        }

        if (addDiagnostic)
        {
            if (this.IsEmpty)
            {
                if (this.PreviousToken.Kind != TokenKind.Invalid)
                {
                    var r = this.PreviousToken.Range;
                    this.Diagnostic.Add(new(r.End, r.End), Hashed.Kimi.MissingExpectedToken, targetKind.ToText());
                }
            }
        }

        range = default;
        return false;
    }

    public bool SkipUntil(TokenKind kind1, TokenKind kind2)
    {
        while (this.TryGetCurrentToken(out var token))
        {
            var tokenKind = token.Kind;
            if (tokenKind == kind1 || tokenKind == kind2)
            {
                return true;
            }

            this.AdvanceOne();
        }

        return false;
    }

    public bool TryConsumeIdentifier(ReadOnlySpan<char> name)
    {
        if (this.TryGetCurrentToken(out var token) &&
            token.Kind == TokenKind.Identifier &&
            token.Text.Span.Equals(name, StringComparison.Ordinal))
        {
            this.AdvanceOne();
            return true;
        }

        return false;
    }

    public bool TryReadIdentifier(out Token token)
    {
        if (this.TryGetCurrentToken(out token) &&
            token.Kind == TokenKind.Identifier)
        {
            this.AdvanceOne();
            return true;
        }

        token = default;
        return false;
    }

    public bool Advance()
    {
        if (this.Position >= this.count)
        {
            return false;
        }

        if (this.currentSpanIndex >= this.currentSpan.Length)
        {
            if (!this.MoveToNextNonEmptySpan())
            {
                return false;
            }
        }

        this.AdvanceOne();
        return true;
    }

    public SourceRange CurrentRange()
    {
        if (this.TryGetCurrentToken(out var token))
        {
            return token.Range;
        }

        if (this.PreviousToken.Kind != TokenKind.Invalid)
        {
            var range = this.PreviousToken.Range;
            return new(range.End, range.End);
        }

        return default;
    }

    public void ReportUnexpectedToken(Token token)
    {
        this.Diagnostic.AddToken(token, Hashed.Kimi.UnmatchedToken, token.Kind.ToText());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetCurrentToken(out Token token)
    {
        if (this.Position >= this.count)
        {
            token = default;
            return false;
        }

        if (this.currentSpanIndex >= this.currentSpan.Length)
        {
            if (!this.MoveToNextNonEmptySpan())
            {
                token = default;
                return false;
            }
        }

        token = this.currentSpan[this.currentSpanIndex];
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AdvanceOne()
    {
        Debug.Assert(this.Position < this.count);
        Debug.Assert(this.currentSpanIndex < this.currentSpan.Length);

        this.PreviousToken = this.currentSpan[this.currentSpanIndex];

        this.currentSpanIndex++;
        this.Position++;
    }

    private bool MoveToNextNonEmptySpan()
    {
        while (this.sequence.TryGet(ref this.nextSegmentPosition, out var memory, advance: true))
        {
            if (!memory.IsEmpty)
            {
                this.currentSpan = memory.Span;
                this.currentSpanIndex = 0;
                return true;
            }
        }

        this.currentSpan = default;
        this.currentSpanIndex = 0;
        return false;
    }
}
