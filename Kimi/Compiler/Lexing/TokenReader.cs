// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Lexing;

/// <summary>
/// Represents parser context temporarily detached from a <see cref="TokenReader"/>.
/// </summary>
/// <param name="AttributeKoto">The current attribute chain.</param>
/// <param name="ModifierKind">The current modifiers.</param>
/// <param name="IsExcluded">Whether the current declaration is excluded.</param>
public readonly record struct TokenContext(AttributeKoto? AttributeKoto, ModifierKind ModifierKind, bool IsExcluded);

/// <summary>
/// Provides sequential access to tokens produced by a <see cref="Tokenizer"/>.
/// </summary>
public ref struct TokenReader
{
    #region FieldsAndProperties

    /// <summary>
    /// Gets the code context associated with this reader.
    /// </summary>
    public readonly CodeContext CodeContext;

    /// <summary>
    /// Gets the source document associated with the token sequence.
    /// </summary>
    public readonly SourceDocument SourceDocument;

    private readonly ReadOnlySpan<char> sourceText;
    private readonly ReadOnlySequence<Token> sequence;
    private readonly int length;

    private SequencePosition nextSegmentPosition;
    private ReadOnlySpan<Token> currentSpan;
    private int currentSpanIndex;
    private Token currentToken;

    /// <summary>
    /// Gets the current token position.
    /// </summary>
    public int Position { get; private set; }

    /// <summary>
    /// Gets the current attribute chain.
    /// </summary>
    public AttributeKoto? AttributeKoto { get; private set; }

    /// <summary>
    /// Gets the modifiers associated with the current declaration.
    /// </summary>
    public ModifierKind ModifierKind { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the current declaration is excluded.
    /// </summary>
    public bool IsExcluded { get; internal set; }

    /// <summary>
    /// Gets the diagnostic collection associated with this reader.
    /// </summary>
    public readonly DiagnosticCollection Diagnostic => this.CodeContext.DiagnosticCollection;

    /// <summary>
    /// Gets the total number of tokens.
    /// </summary>
    public readonly int Length => this.length;

    /// <summary>
    /// Gets the number of unread tokens.
    /// </summary>
    public readonly int Remaining => this.length - this.Position;

    /// <summary>
    /// Gets a value indicating whether another token can be read.
    /// </summary>
    public readonly bool CanRead => this.Position < this.length;

    /// <summary>
    /// Gets a value indicating whether all tokens have been consumed.
    /// </summary>
    public readonly bool IsEnd => this.Position >= this.length;

    /// <summary>
    /// Gets the current token.
    /// </summary>
    public readonly Token CurrentToken => this.currentToken;

    /// <summary>
    /// Gets the kind of the current token.
    /// </summary>
    public readonly TokenKind CurrentTokenKind => this.currentToken.Kind;

    /// <summary>
    /// Gets the source range of the current token.
    /// </summary>
    public readonly SourceSpan CurrentTokenRange => this.currentToken.Range;

    /// <summary>
    /// Gets the source length of the current token.
    /// </summary>
    public readonly int CurrentTokenLength => this.currentToken.Length;

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenReader"/> struct.
    /// </summary>
    /// <param name="codeContext">The code context.</param>
    /// <param name="tokenizer">The tokenizer containing the token sequence.</param>
    internal TokenReader(CodeContext codeContext, ref Tokenizer tokenizer)
    {
        this.CodeContext = codeContext;
        this.SourceDocument = tokenizer.SourceDocument;

        var tokenSequence = tokenizer.ToReadOnlySequence();
        this.sourceText = this.SourceDocument.AsSpan();
        this.sequence = tokenSequence;
        this.length = checked((int)tokenSequence.Length);

        this.Position = 0;

        this.AttributeKoto = default;
        this.ModifierKind = default;
        this.IsExcluded = default;

        this.nextSegmentPosition = tokenSequence.Start;
        this.currentSpan = default;
        this.currentSpanIndex = 0;
        this.currentToken = default;

        this.MoveToNextNonEmptySpan();
    }

    /// <summary>
    /// Clears the current parser context.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearContext()
    {
        this.AttributeKoto = default;
        this.ModifierKind = default;
        this.IsExcluded = false;
    }

    /// <summary>
    /// Detaches and returns the current parser context.
    /// </summary>
    /// <returns>The detached parser context.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TokenContext TakeContext()
    {
        var context = new TokenContext(this.AttributeKoto, this.ModifierKind, this.IsExcluded);

        this.AttributeKoto = default;
        this.ModifierKind = default;
        this.IsExcluded = false;

        return context;
    }

    /// <summary>
    /// Restores a previously detached parser context.
    /// </summary>
    /// <param name="context">The parser context to restore.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RestoreContext(TokenContext context)
    {
        this.AttributeKoto = context.AttributeKoto;
        this.ModifierKind = context.ModifierKind;
        this.IsExcluded = context.IsExcluded;
    }

    /// <summary>
    /// Adds an attribute to the current attribute chain.
    /// </summary>
    /// <param name="attributeKoto">The attribute to add.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PushAttribute(AttributeKoto attributeKoto)
    {
        if (this.AttributeKoto is not null)
        {
            attributeKoto.AttributeChain = this.AttributeKoto;
        }

        this.AttributeKoto = attributeKoto;
    }

    /// <summary>
    /// Removes and returns the current attribute chain.
    /// </summary>
    /// <returns>The current attribute chain, or <see langword="null"/> if none exists.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AttributeKoto? PopAttribute()
    {
        var attributeKoto = this.AttributeKoto;
        this.AttributeKoto = default;
        return attributeKoto;
    }

    /// <summary>
    /// Reads the current token and advances to the next token.
    /// </summary>
    /// <param name="token">The token that was read.</param>
    /// <param name="addDiagnostic">Whether to report a diagnostic when the syntax is incomplete.</param>
    /// <returns><see langword="true"/> if a token was read; otherwise, <see langword="false"/>.</returns>
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
            this.AddDiagnostic(KimiDiagnostic.IncompleteSyntax_Kd);
        }

        token = default;
        return false;
    }

    /// <summary>
    /// Consumes a token of the specified kind.
    /// </summary>
    /// <param name="targetKind">The expected token kind.</param>
    /// <param name="range">The source range of the consumed token.</param>
    /// <param name="addDiagnostic">Whether to report a diagnostic when the expected token is not found.</param>
    /// <returns><see langword="true"/> if the expected token was consumed; otherwise, <see langword="false"/>.</returns>
    public bool TryConsume(TokenKind targetKind, out SourceSpan range, bool addDiagnostic = true)
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

            if (token.Kind == TokenKind.Sharp)
            {
                // Attributes may appear between the caller and the expected token.
                _ = Parser.ParseAttributeKoto(ref this);
                goto Loop;
            }

            if (addDiagnostic)
            {
                this.Diagnostic.Add(token.Range, KimiDiagnostic.TokenMismatch_Kd, targetKind.ToText());
                this.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, 0);
            }

            range = default;
            return false;
        }

        if (addDiagnostic && this.IsEnd && this.CurrentTokenKind != TokenKind.Invalid)
        {
            var r = this.CurrentTokenRange;
            this.Diagnostic.Add(new(r.End, 0), KimiDiagnostic.MissingExpectedToken_Kd, targetKind.ToText());
        }

        range = default;
        return false;
    }

    /// <summary>
    /// Advances until the specified token kind is reached.
    /// </summary>
    /// <param name="kind1">The token kind at which to stop.</param>
    /// <param name="kimiDiagnostic">An optional diagnostic hash reported for the first skipped token.</param>
    /// <returns>The token kind that stopped the scan, or the default value if the end was reached.</returns>
    public TokenKind SkipUntil(TokenKind kind1, KimiDiagnostic kimiDiagnostic)
    {
        while (this.CanRead)
        {
            if (this.currentToken.Kind == kind1)
            {
                return this.currentToken.Kind;
            }

            if (kimiDiagnostic != 0)
            {
                this.AddDiagnostic(kimiDiagnostic, this.GetSpan(this.currentToken).ToString());
                kimiDiagnostic = 0;
            }

            this.AdvanceOne();
        }

        return default;
    }

    /// <summary>
    /// Advances until either of the specified token kinds is reached.
    /// </summary>
    /// <param name="kind1">The first token kind at which to stop.</param>
    /// <param name="kind2">The second token kind at which to stop.</param>
    /// <param name="kimiDiagnostic">An optional diagnostic hash reported for the first skipped token.</param>
    /// <returns>The token kind that stopped the scan, or the default value if the end was reached.</returns>
    public TokenKind SkipUntil(TokenKind kind1, TokenKind kind2, KimiDiagnostic kimiDiagnostic = KimiDiagnostic.Template_Kd)
    {
        while (this.CanRead)
        {
            var tokenKind = this.currentToken.Kind;
            if (tokenKind == kind1 || tokenKind == kind2)
            {
                return tokenKind;
            }

            if (kimiDiagnostic != 0)
            {
                this.AddDiagnostic(kimiDiagnostic, this.GetSpan(this.currentToken).ToString());
                kimiDiagnostic = 0;
            }

            this.AdvanceOne();
        }

        return default;
    }

    /// <summary>
    /// Skips the current block while respecting nested blocks.
    /// </summary>
    /// <param name="isRootGroup">
    /// <see langword="true"/> to skip until the next root group;
    /// otherwise, to skip the current nested block.
    /// </param>
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

        if (this.IsEnd || this.currentToken.Kind != TokenKind.StartBlock)
        {
            return;
        }

        this.AdvanceOne();
        var depth = 1;

        while (this.CanRead)
        {
            if (this.currentToken.Kind == TokenKind.StartBlock)
            {
                depth++;
            }
            else if (this.currentToken.Kind == TokenKind.EndBlock)
            {
                depth--;
                if (depth == 0)
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

    /// <summary>
    /// Advances to the next token.
    /// </summary>
    /// <returns><see langword="true"/> if a token was consumed; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Advance()
    {
        if (!this.CanRead)
        {
            return false;
        }

        this.AdvanceOne();
        return true;
    }

    /// <summary>
    /// Reports an unexpected-token diagnostic.
    /// </summary>
    /// <param name="token">The unexpected token.</param>
    public void ReportUnexpectedToken(Token token)
    {
        this.Diagnostic.Add(token.Range, KimiDiagnostic.UnmatchedToken_Kd, token.Kind.ToText());
    }

    /// <summary>
    /// Adds a diagnostic for the current token.
    /// </summary>
    /// <param name="kimiDiagnostic">The diagnostic.</param>
    /// <param name="obj">An optional diagnostic argument.</param>
    /// <param name="obj2">An optional diagnostic argument 2.</param>
    public void AddDiagnostic(KimiDiagnostic kimiDiagnostic, object? obj = null, object? obj2 = null)
    {
        if (this.CanRead)
        {
            this.Diagnostic.Add(this.currentToken.Range, kimiDiagnostic, obj, obj2);
        }
    }

    /// <summary>
    /// Determines whether the specified token is an identifier with the given text.
    /// </summary>
    /// <param name="token">The token to examine.</param>
    /// <param name="identifier">The expected identifier text.</param>
    /// <returns><see langword="true"/> if the token matches the identifier; otherwise, <see langword="false"/>.</returns>
    public readonly bool IsIdentifierToken(Token token, ReadOnlySpan<char> identifier)
    {
        return token.Kind == TokenKind.Identifier &&
            this.GetSpan(token).SequenceEqual(identifier);
    }

    /// <summary>
    /// Creates an error node at the current token.
    /// </summary>
    /// <returns>A new error node.</returns>
    public ErrorKoto NewErrorKoto()
    {
        return new ErrorKoto(ref this, this.CurrentToken.Range);
    }

    /// <summary>
    /// Gets the source text represented by the specified token.
    /// </summary>
    /// <param name="token">The token whose source text is requested.</param>
    /// <returns>The source span represented by the token.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ReadOnlySpan<char> GetSpan(Token token)
    {
        return this.sourceText.Slice(token.Start, token.Length);
    }

    /// <summary>
    /// Returns the textual representation of the current token.
    /// </summary>
    /// <returns>The textual representation of the current token.</returns>
    public readonly override string ToString()
    {
        // return this.CurrentToken.ToString();
        return this.GetSpan(this.CurrentToken).ToString();
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
            return;
        }

        if (this.Position < this.length)
        {
            // Keep the current token synchronized when crossing sequence segments.
            var result = this.MoveToNextNonEmptySpan();
            Debug.Assert(result);
            return;
        }

        this.currentSpan = default;
        this.currentSpanIndex = 0;
        this.currentToken = Token.Invalid;
    }

    private bool MoveToNextNonEmptySpan()
    {
        while (this.sequence.TryGet(ref this.nextSegmentPosition, out var memory, advance: true))
        {
            if (!memory.IsEmpty)
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
