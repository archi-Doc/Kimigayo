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
{// 144
    public const int MaxDepth = 10;

    #region FieldAndProperty

    public readonly DiagnosticCollection Diagnostic;

    public readonly CodeContext CodeContext;

    private readonly ReadOnlySequence<Token> sequence;
    private readonly int length;

    private SequencePosition nextSegmentPosition;
    private ReadOnlySpan<Token> currentSpan;
    private int currentSpanIndex;

    public Token CurrentToken { get; private set; }

    public int Position { get; private set; }

    public int Depth { get; private set; }

    public AttributeKoto? AttributeKoto { get; private set; }

    public ModifierKind ModifierKind { get; internal set; }

    public bool IsExcluded { get; internal set; }

    public readonly int Length => this.length;

    public readonly int Remaining => this.length - this.Position;

    public readonly bool CanRead => this.Position < this.length;

    public readonly bool IsEnd => this.Position >= this.length;

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
        //var x = Unsafe.SizeOf<TokenReader>();
        this.Diagnostic = diagnostic;
        this.CodeContext = codeContext;

        this.sequence = tokenSequence;
        this.length = checked((int)tokenSequence.Length);

        this.Position = 0;
        this.Depth = 0;

        this.nextSegmentPosition = tokenSequence.Start;
        this.currentSpan = default;
        this.currentSpanIndex = 0;

        this.PreviousToken = default;

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

    public void Consume(out Token token)
    {
        this.Clear();

        while (this.TryRead(out token) &&
            token.Kind == TokenKind.Separator)
        {// Skip Separator
        }
    }

    public bool TryConsume(TokenKind targetKind, out SourceRange range, bool addDiagnostic = true)
    {
Loop:
        if (this.TryGetCurrentToken(out var token))
        {
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
            }

            range = default;
            return false;
        }

        if (addDiagnostic)
        {
            if (this.IsEnd)
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

    public TokenKind SkipUntil(TokenKind kind1, ulong hash = 0)
    {
        while (this.TryGetCurrentToken(out var token))
        {
            if (token.Kind == kind1)
            {
                return token.Kind;
            }

            if (hash != 0)
            {
                this.AddDiagnostic(hash, token.Span.ToString());
                hash = 0;
            }

            this.AdvanceOne();
        }

        return default;
    }

    public TokenKind SkipUntil(TokenKind kind1, TokenKind kind2, ulong hash = 0)
    {
        while (this.TryGetCurrentToken(out var token))
        {
            var tokenKind = token.Kind;
            if (tokenKind == kind1 || tokenKind == kind2)
            {
                return token.Kind;
            }

            if (hash != 0)
            {
                this.AddDiagnostic(hash, token.Span.ToString());
            }

            this.AdvanceOne();
        }

        return default;
    }

    public void SkipCurrentBlock(bool isRootGroup)
    {
        Token token;
        if (isRootGroup)
        {
            while (this.TryGetCurrentToken(out token))
            {
                if (token.Kind == TokenKind.RootGroup)
                {
                    return;
                }

                this.AdvanceOne();
            }

            return;
        }

        if (!this.TryGetCurrentToken(out token))
        {
            return;
        }

        if (token.Kind != TokenKind.StartBlock)
        {
            return;
        }

        this.AdvanceOne();
        var indent = 1;

        while (this.TryGetCurrentToken(out token))
        {
            if (token.Kind == TokenKind.StartBlock)
            {
                indent++;
            }
            else if (token.Kind == TokenKind.EndBlock)
            {
                indent--;
                if (indent <= 0)
                {
                    this.AdvanceOne();
                    return;
                }
            }
            else if (token.Kind == TokenKind.RootGroup)
            {
                return;
            }

            this.AdvanceOne();
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

    public void AddDiagnostic(ulong diagnosticHash, object? obj = null)
    {
        if (this.TryGetCurrentToken(out var token))
        {
            this.Diagnostic.AddToken(token, diagnosticHash, obj);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetCurrentToken(out Token token)
    {
        if (this.Position >= this.length)
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
        Debug.Assert(this.Position < this.length);
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
