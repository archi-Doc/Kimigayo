// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Arc.Crypto;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Lexing;

public readonly record struct TokenState(AttributeKoto? AttributeKoto, ModifierKind ModifierKind, bool IsExcluded);

public ref struct TokenReader
{// 136
    #region FieldAndProperty

    public readonly DiagnosticCollection Diagnostic;

    public readonly CodeContext CodeContext;

    private readonly ReadOnlySequence<Token> sequence;
    private readonly int length;

    private SequencePosition nextSegmentPosition;
    private ReadOnlySpan<Token> currentSpan;
    private int currentSpanIndex;
    private Token currentToken;

    public int Position { get; private set; }

    // public int Depth { get; private set; }

    public AttributeKoto? AttributeKoto { get; private set; }

    public ModifierKind ModifierKind { get; internal set; }

    public bool IsExcluded { get; internal set; }

    public readonly int Length => this.length;

    public readonly int Remaining => this.length - this.Position;

    public readonly bool CanRead => this.Position < this.length;

    public readonly bool IsEnd => this.Position >= this.length;

    public Token CurrentToken => this.currentToken;

    public TokenKind CurrentTokenKind => this.currentToken.Kind;

    public SourceRange CurrentTokenRange => this.currentToken.Range;

    #endregion

    public TokenReader(DiagnosticCollection diagnostic, CodeContext codeContext, ReadOnlySequence<Token> tokenSequence)
    {
        this.Diagnostic = diagnostic;
        this.CodeContext = codeContext;

        this.sequence = tokenSequence;
        this.length = checked((int)tokenSequence.Length);

        this.Position = 0;

        this.nextSegmentPosition = tokenSequence.Start;
        this.currentSpan = default;
        this.currentSpanIndex = 0;

        this.MoveToNextNonEmptySpan();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        this.AttributeKoto = default;
        this.ModifierKind = default;
        this.IsExcluded = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TokenState StoreState()
    {
        var state = new TokenState(this.AttributeKoto, this.ModifierKind, this.IsExcluded);
        this.AttributeKoto = default;
        this.ModifierKind = default;
        this.IsExcluded = default;

        return state;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RestoreState(TokenState state)
    {
        (this.AttributeKoto, this.ModifierKind, this.IsExcluded) = (state.AttributeKoto, state.ModifierKind, state.IsExcluded);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushAttribute(AttributeKoto attributeKoto)
    {
        if (this.AttributeKoto is not null)
        {
            attributeKoto.AttributeChain = this.AttributeKoto;
        }

        this.AttributeKoto = attributeKoto;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AttributeKoto? PopAttribute()
    {
        var attributeKoto = this.AttributeKoto;
        this.AttributeKoto = default;
        return attributeKoto;
    }

    /*[MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    }*/

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryRead(out Token token)
    {
        if (this.CanRead)
        {
            token = this.currentToken;
            this.AdvanceOne();
            return true;
        }

        token = default;
        return false;
    }

    /*public bool TryPeek(out Token token)
    {
        if (this.TryGetCurrentToken(out token))
        {
            return true;
        }

        token = default;
        return false;
    }

    public void Consume(out Token token)
    {
        this.Clear();

        while (this.TryRead(out token) &&
            token.Kind == TokenKind.Separator)
        {// Skip Separator
        }
    }*/

    public bool TryConsume(TokenKind targetKind, out SourceRange range, bool addDiagnostic = true)
    {
Loop:
        if (this.CanRead)
        {
            var token = this.currentToken;
            if (token.Kind == targetKind)
            {
                range = token.Range;
                this.AdvanceOne();
                return true;
            }
            else if (token.Kind == TokenKind.Sharp)
            {// Skip attribute
                _ = KotoParser.ParseAttributeKoto(ref this);
                goto Loop;
            }

            if (addDiagnostic)
            {
                this.Diagnostic.AddToken(token, Hashed.Kimi.TokenMismatch, targetKind.ToText());
                this.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
            }

            range = default;
            return false;
        }

        if (addDiagnostic)
        {
            if (this.IsEnd)
            {
                if (this.CurrentTokenKind != TokenKind.Invalid)
                {
                    var r = this.CurrentTokenRange;
                    this.Diagnostic.Add(new(r.End, r.End), Hashed.Kimi.MissingExpectedToken, targetKind.ToText());
                }
            }
        }

        range = default;
        return false;
    }

    public TokenKind SkipUntil(TokenKind kind1, ulong hash = 0)
    {
        while (this.CanRead)
        {
            if (this.currentToken.Kind == kind1)
            {
                return this.currentToken.Kind;
            }

            if (hash != 0)
            {
                this.AddDiagnostic(hash, this.currentToken.Span.ToString());
                hash = 0;
            }

            this.AdvanceOne();
        }

        return default;
    }

    public TokenKind SkipUntil(TokenKind kind1, TokenKind kind2, ulong hash = 0)
    {
        while (this.CanRead)
        {
            var tokenKind = this.currentToken.Kind;
            if (tokenKind == kind1 || tokenKind == kind2)
            {
                return this.currentToken.Kind;
            }

            if (hash != 0)
            {
                this.AddDiagnostic(hash, this.currentToken.Span.ToString());
                hash = 0;
            }

            this.AdvanceOne();
        }

        return default;
    }

    public void SkipCurrentBlock(bool isRootGroup)
    {
        if (isRootGroup)
        {
            while (this.CanRead)
            {
                if (this.currentToken.Kind == TokenKind.RootGroup)
                {
                    return;
                }

                this.AdvanceOne();
            }

            return;
        }

        if (this.IsEnd)
        {
            return;
        }

        if (this.currentToken.Kind != TokenKind.StartBlock)
        {
            return;
        }

        this.AdvanceOne();
        var indent = 1;

        while (this.CanRead)
        {
            if (this.currentToken.Kind == TokenKind.StartBlock)
            {
                indent++;
            }
            else if (this.currentToken.Kind == TokenKind.EndBlock)
            {
                indent--;
                if (indent <= 0)
                {
                    this.AdvanceOne();
                    return;
                }
            }
            else if (this.currentToken.Kind == TokenKind.RootGroup)
            {
                return;
            }

            this.AdvanceOne();
        }
    }

    public ReferenceKind ReadReferenceKind()
    {
        var token = this.CurrentToken;
        if (token.Kind == TokenKind.Identifier)
        {
            this.Advance();
            var referenceKind = token.Span.ToReferenceKind();
            if (referenceKind == ReferenceKind.None)
            {
                this.AddDiagnostic(Hashed.Kimi.InvalidReferenceSyntax, token.Span.ToString());
                referenceKind = ReferenceKind.Borrow;
            }

            return referenceKind;
        }
        else
        {
            return ReferenceKind.Borrow;
        }
    }

    /*public bool TryConsumeIdentifier(ReadOnlySpan<char> name)
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
    }*/

    public bool Advance()
    {
        if (this.Position >= this.length)
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

    public void ReportUnexpectedToken(Token token)
    {
        this.Diagnostic.AddToken(token, Hashed.Kimi.UnmatchedToken, token.Kind.ToText());
    }

    public void AddDiagnostic(ulong diagnosticHash, object? obj = null)
    {
        if (this.CanRead)
        {
            this.Diagnostic.AddToken(this.currentToken, diagnosticHash, obj);
        }
    }

    public ErrorKoto NewErrorKoto()
    {
        return new ErrorKoto(ref this, this.CurrentToken.Range);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AdvanceOne()
    {
        Debug.Assert(this.Position < this.length);
        Debug.Assert(this.currentSpanIndex < this.currentSpan.Length);

        this.currentSpanIndex++;
        this.Position++;
        this.currentToken = this.currentSpan[this.currentSpanIndex];
    }

    private bool MoveToNextNonEmptySpan()
    {
        while (this.sequence.TryGet(ref this.nextSegmentPosition, out var memory, advance: true))
        {
            if (memory.IsEmpty)
            {
                // this.currentToken = default;
            }
            else
            {
                this.currentSpan = memory.Span;
                this.currentSpanIndex = 0;
                this.currentToken = this.currentSpan[0];
                return true;
            }
        }

        this.currentSpan = default;
        this.currentSpanIndex = 0;
        return false;
    }
}
