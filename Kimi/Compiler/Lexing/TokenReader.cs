// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Lexing;

public readonly record struct TokenState(AttributeKoto? AttributeKoto, ModifierKind ModifierKind, bool IsExcluded);

public ref struct TokenReader
{// 136
    #region FieldAndProperty

    public readonly CodeContext CodeContext;

    private readonly ReadOnlySpan<char> sourceText;
    private readonly ReadOnlySequence<Token> sequence;
    private readonly int length;

    private SequencePosition nextSegmentPosition;
    private ReadOnlySpan<Token> currentSpan;
    private int currentSpanIndex;
    private Token currentToken;

    public int Position { get; private set; }

    public AttributeKoto? AttributeKoto { get; private set; }

    public ModifierKind ModifierKind { get; internal set; }

    public bool IsExcluded { get; internal set; }

    public DiagnosticCollection Diagnostic => this.CodeContext.DiagnosticCollection;

    public readonly int Length => this.length;

    public readonly int Remaining => this.length - this.Position;

    public readonly bool CanRead => this.Position < this.length;

    public readonly bool IsEnd => this.Position >= this.length;

    public Token CurrentToken => this.currentToken;

    public TokenKind CurrentTokenKind => this.currentToken.Kind;

    public SourceRange CurrentTokenRange => this.currentToken.Range;

    public int CurrentTokenLength => this.currentToken.Length;

    #endregion

    internal TokenReader(CodeContext codeContext, ref Tokenizer tokenizer)
    {
        this.CodeContext = codeContext;

        var tokenSequence = tokenizer.ToReadOnlySequence();
        this.sourceText = tokenizer.SourceText;
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
    public bool TryRead(out Token token, bool addDiagnostic = true)
    {
        if (this.CanRead)
        {
            token = this.currentToken;
            this.AdvanceOne();
            return true;
        }

        if (addDiagnostic)
        {
            this.AddDiagnostic(Hashed.Kimi.IdentifierExpected);
        }

        token = default;
        return false;
    }

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
                this.AddDiagnostic(hash, this.GetSpan(this.currentToken).ToString());
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
                this.AddDiagnostic(hash, this.GetSpan(this.currentToken).ToString());
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
            var span = this.GetSpan(token);
            var referenceKind = span.ToReferenceKind();
            if (referenceKind == ReferenceKind.None)
            {
                this.AddDiagnostic(Hashed.Kimi.InvalidReferenceSyntax, span.ToString());
                referenceKind = ReferenceKind.Borrow;
            }

            return referenceKind;
        }
        else
        {
            return ReferenceKind.Borrow;
        }
    }

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

    public bool IsIdentifierToken(Token token, ReadOnlySpan<char> identifier)
    {
        return token.Kind == TokenKind.Identifier &&
            this.GetSpan(token).SequenceEqual(identifier);
    }

    public ErrorKoto NewErrorKoto()
    {
        return new ErrorKoto(ref this, this.CurrentToken.Range);
    }

    public ReadOnlySpan<char> GetSpan(Token token)
    {
        return this.sourceText.Slice(token.Start, token.Length);
    }

    public override string ToString()
    {
        return this.CurrentToken.ToString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AdvanceOne()
    {
        Debug.Assert(this.Position < this.length);
        Debug.Assert(this.currentSpanIndex < this.currentSpan.Length);

        this.currentSpanIndex++;
        this.Position++;
        if (this.currentSpanIndex < this.currentSpan.Length)
        {
            this.currentToken = this.currentSpan[this.currentSpanIndex];
        }
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
