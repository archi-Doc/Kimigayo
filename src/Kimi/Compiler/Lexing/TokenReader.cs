// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
/// <remarks>
/// The reader is a cursor over a contiguous token span. Lookahead is exposed through
/// <see cref="PeekKind"/> and <see cref="TrySkipSeparatorsTo"/> instead of copying the reader.
/// </remarks>
public ref partial struct TokenReader
{
    #region FieldsAndProperties

    /// <summary>
    /// Gets the code context associated with this reader.
    /// </summary>
    public readonly CodeContext CodeContext;

    private readonly Compilation compilation;
    private readonly ReadOnlySpan<char> sourceText;
    private readonly ReadOnlySpan<Token> tokens;
    private readonly Token endToken;
    private Token currentToken;

    // The last form reported missing and its insertion point: later non-closer forms expected there are its consequences.
    private (int At, DiagnosticKey Key)? lastMissing;

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
    public readonly int Length => this.tokens.Length;

    /// <summary>
    /// Gets the number of unread tokens.
    /// </summary>
    public readonly int Remaining => this.tokens.Length - this.Position;

    /// <summary>
    /// Gets a value indicating whether another token can be read.
    /// </summary>
    public readonly bool CanRead => this.Position < this.tokens.Length;

    /// <summary>
    /// Gets a value indicating whether all tokens have been consumed.
    /// </summary>
    public readonly bool IsEnd => this.Position >= this.tokens.Length;

    /// <summary>
    /// Gets the current token. At the end of the sequence this is an invalid token positioned at the end of the source.
    /// </summary>
    public readonly Token CurrentToken => this.currentToken;

    /// <summary>
    /// Gets the kind of the current token.
    /// </summary>
    public readonly TokenKind CurrentTokenKind => this.currentToken.Kind;

    /// <summary>
    /// Gets the source range of the current token, or an empty range at the end of the source.
    /// </summary>
    public readonly SourceSpan CurrentTokenRange => this.currentToken.Span;

    /// <summary>
    /// Gets the source length of the current token.
    /// </summary>
    public readonly int CurrentTokenLength => this.currentToken.Length;

    // Region-local parsing restrictions; grouping and arm/item boundaries reset these.
    internal bool SingleBodyRegion { get; set; }

    internal bool IfBodyRegion { get; set; }

    internal bool HeaderRegion { get; set; }

    internal bool ConstraintRequirement { get; set; }

    internal readonly bool SameLine(int end, int start)
        => start >= end && !this.sourceText[end..start].ContainsAny('\r', '\n');

    internal readonly int PreviousEnd => this.Position > 0 ? this.tokens[this.Position - 1].Span.End : 0;

    internal bool AllowArrayElementInference { get; set; }

    internal bool HasInferredArrayElement { get; set; }

    internal int DocumentationExcludedStart { get; set; }

    /// <summary>Gets or sets the number of excluded-syntax regions enclosing the current position (SPEC 19.5).</summary>
    /// <remarks>Excluded syntax is parsed and source-checked, but nothing inside it selects, registers or associates documentation.</remarks>
    internal int ExclusionDepth { get; set; }

    /// <summary>Gets or sets the directive that excludes the current excluded region: an <c>#if</c> Condition or a <c>#case</c> header.</summary>
    internal SourceSpan ExcludingDirective { get; set; }

    /// <summary>Gets or sets the innermost directive that excludes the pending item, meaningful while <see cref="IsExcluded"/> holds.</summary>
    internal SourceSpan PendingExclusion { get; set; }

    /// <summary>Gets a value indicating whether the current position lies inside excluded syntax.</summary>
    internal readonly bool InExcludedSyntax => this.ExclusionDepth > 0;

    /// <summary>Gets the end of the last written token before the current position: where a missing form is inserted.</summary>
    internal readonly int PreviousSyntaxEnd
    {
        get
        {
            for (var i = this.Position - 1; i >= 0; i--)
            {
                if (!this.tokens[i].IsMissing && this.tokens[i].Kind is not (TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock))
                {
                    return TokenHelper.WrittenEnd(this.tokens[i], this.sourceText);
                }
            }

            return 0;
        }
    }

    /// <summary>Gets the insertion point at the end of the current line: after the last written token before the next line boundary.</summary>
    internal readonly int LineEndInsertionPoint
    {
        get
        {
            var last = -1;
            for (var i = this.Position; i < this.tokens.Length; i++)
            {
                if (this.tokens[i].Kind is TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock)
                {
                    break;
                }

                if (!this.tokens[i].IsMissing)
                {
                    last = i;
                }
            }

            return last < 0 ? this.PreviousSyntaxEnd : TokenHelper.WrittenEnd(this.tokens[last], this.sourceText);
        }
    }

    internal readonly void Document(Koto declaration, SourceSpan header, AttributeKoto? attributes = null)
    {
        // Excluded syntax receives no documentation association (SPEC 2.3.3).
        if (!this.InExcludedSyntax)
        {
            this.CodeContext.Documentation?.Associate(declaration, header, attributes, this.tokens);
        }
    }

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenReader"/> struct.
    /// </summary>
    /// <param name="codeContext">The code context.</param>
    /// <param name="tokenizer">The tokenizer containing the token sequence.</param>
    internal TokenReader(CodeContext codeContext, ref Tokenizer tokenizer)
    {
        this.CodeContext = codeContext;
        this.compilation = codeContext.Compilation;
        this.sourceText = tokenizer.SourceText;
        this.tokens = tokenizer.Tokens;
        this.endToken = new Token(TokenKind.Invalid, new SourceSpan(this.sourceText.Length, 0));
        this.currentToken = this.tokens.Length > 0 ? this.tokens[0] : this.endToken;
    }

    /// <summary>Initializes a new instance of the <see cref="TokenReader"/> struct using immutable cached tokens and compilation-local state.</summary>
    /// <param name="codeContext">The current source context.</param>
    /// <param name="sourceText">The complete text whose offsets the tokens reference.</param>
    /// <param name="tokens">An immutable token sequence which outlives this reader.</param>
    internal TokenReader(CodeContext codeContext, ReadOnlySpan<char> sourceText, ReadOnlySpan<Token> tokens)
        : this(codeContext, sourceText, tokens, sourceText.Length)
    {
    }

    private TokenReader(CodeContext codeContext, ReadOnlySpan<char> sourceText, ReadOnlySpan<Token> tokens, int end)
    {
        this.CodeContext = codeContext;
        this.compilation = codeContext.Compilation;
        this.sourceText = sourceText;
        this.tokens = tokens;
        this.endToken = new Token(TokenKind.Invalid, new SourceSpan(end, 0));
        this.currentToken = tokens.Length > 0 ? tokens[0] : this.endToken;
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
        this.HasCompileTimeIfPrefix = false;
    }

    /// <summary>
    /// Detaches and returns the current parser context.
    /// </summary>
    /// <returns>The detached parser context.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TokenContext TakeContext()
    {
        var context = new TokenContext(this.AttributeKoto, this.ModifierKind, this.IsExcluded);
        this.ClearContext();
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
    /// <returns><see langword="true"/> if a token was read; otherwise, <see langword="false"/>, and the caller reports what it expected.</returns>
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

    /// <summary>
    /// Reads the current token, which must exist, and advances to the next token.
    /// </summary>
    /// <returns>The token that was read.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Token Read()
    {
        Debug.Assert(this.CanRead);
        var token = this.currentToken;
        this.AdvanceOne();
        return token;
    }

    /// <summary>
    /// Consumes a token of the specified kind.
    /// </summary>
    /// <param name="targetKind">The expected token kind.</param>
    /// <param name="range">The source range of the consumed token.</param>
    /// <param name="addDiagnostic">Whether to report a diagnostic when the expected token is not found.</param>
    /// <returns><see langword="true"/> if the expected token was consumed; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryConsume(TokenKind targetKind, out SourceSpan range, bool addDiagnostic = true)
    {
        if (this.currentToken.Kind == targetKind && this.CanRead)
        {
            range = this.currentToken.Span;
            this.AdvanceOne();
            return true;
        }

        if (!addDiagnostic && this.currentToken.Kind != TokenKind.Sharp)
        {
            range = default;
            return false;
        }

        return this.TryConsumeWithRecovery(targetKind, out range, addDiagnostic);
    }

    /// <summary>
    /// Consumes the current token when it has the specified kind.
    /// </summary>
    /// <param name="targetKind">The expected token kind.</param>
    /// <returns><see langword="true"/> if the token was consumed; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryConsume(TokenKind targetKind)
    {
        if (this.currentToken.Kind == targetKind && this.CanRead)
        {
            this.AdvanceOne();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the kind of the token at the specified offset from the current token without advancing.
    /// </summary>
    /// <param name="offset">The number of tokens to look ahead. Zero returns the current token kind.</param>
    /// <returns>The token kind, or <see cref="TokenKind.Invalid"/> beyond the end of the sequence.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly TokenKind PeekKind(int offset = 1)
    {
        if (offset == 0)
        {
            return this.currentToken.Kind;
        }

        var index = this.Position + offset;
        return (uint)index < (uint)this.tokens.Length ? this.tokens[index].Kind : TokenKind.Invalid;
    }

    /// <summary>
    /// Skips separators when they are followed by a token of the specified kind.
    /// </summary>
    /// <remarks>The reader is left unchanged when the next non-separator token differs from <paramref name="kind"/>.</remarks>
    /// <param name="kind">The token kind that must follow the separators.</param>
    /// <returns><see langword="true"/> when the reader now points at a token of the specified kind.</returns>
    public bool TrySkipSeparatorsTo(TokenKind kind)
    {
        if (this.currentToken.Kind == kind)
        {
            return true;
        }

        if (this.currentToken.Kind != TokenKind.Separator)
        {
            return false;
        }

        var index = this.Position;
        var tokens = this.tokens;
        while ((uint)index < (uint)tokens.Length && tokens[index].Kind == TokenKind.Separator)
        {
            index++;
        }

        if ((uint)index >= (uint)tokens.Length || tokens[index].Kind != kind)
        {
            return false;
        }

        this.MoveTo(index);
        return true;
    }

    /// <summary>
    /// Advances past any separator tokens.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SkipSeparators()
    {
        while (this.currentToken.Kind == TokenKind.Separator)
        {
            this.AdvanceOne();
        }
    }

    /// <summary>
    /// Advances until the specified token kind is reached.
    /// </summary>
    /// <param name="kind1">The token kind at which to stop.</param>
    /// <returns>The token kind that stopped the scan, or the default value if the end was reached.</returns>
    public TokenKind SkipUntil(TokenKind kind1)
        => this.SkipUntil(kind1, kind1, kind1);

    /// <summary>
    /// Advances until either of the specified token kinds is reached.
    /// </summary>
    /// <param name="kind1">The first token kind at which to stop.</param>
    /// <param name="kind2">The second token kind at which to stop.</param>
    /// <returns>The token kind that stopped the scan, or the default value if the end was reached.</returns>
    public TokenKind SkipUntil(TokenKind kind1, TokenKind kind2)
        => this.SkipUntil(kind1, kind2, kind2);

    /// <summary>
    /// Advances until any of the specified token kinds is reached.
    /// </summary>
    /// <param name="kind1">The first token kind at which to stop.</param>
    /// <param name="kind2">The second token kind at which to stop.</param>
    /// <param name="kind3">The third token kind at which to stop.</param>
    /// <returns>The token kind that stopped the scan, or the default value if the end was reached.</returns>
    public TokenKind SkipUntil(TokenKind kind1, TokenKind kind2, TokenKind kind3)
    {
        while (this.CanRead)
        {
            var tokenKind = this.currentToken.Kind;
            if (tokenKind == kind1 || tokenKind == kind2 || tokenKind == kind3)
            {
                return tokenKind;
            }

            this.AdvanceOne();
        }

        return default;
    }

    /// <summary>
    /// Advances to the start of the immediately following block without reporting; the caller has reported what it
    /// expected. Stops before a subsequent statement.
    /// </summary>
    /// <returns>
    /// <see cref="TokenKind.StartBlock"/> when a block was found;
    /// otherwise, the default value.
    /// </returns>
    public TokenKind SkipUntilStartBlock()
    {
        var reachedNextStatement = false;
        while (this.CanRead)
        {
            var tokenKind = this.currentToken.Kind;
            if (tokenKind == TokenKind.StartBlock)
            {
                return tokenKind;
            }

            if (tokenKind == TokenKind.EndBlock)
            {
                return default;
            }

            if (tokenKind == TokenKind.Separator)
            {
                reachedNextStatement = true;
                this.AdvanceOne();
                continue;
            }

            if (reachedNextStatement)
            {
                return default;
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
            this.SkipUntil(TokenKind.RootGroup);
            return;
        }

        if (!this.TryConsume(TokenKind.StartBlock))
        {
            return;
        }

        var depth = 1;
        while (this.CanRead)
        {
            var kind = this.currentToken.Kind;
            if (kind == TokenKind.StartBlock)
            {
                depth++;
            }
            else if (kind == TokenKind.EndBlock)
            {
                depth--;
                if (depth == 0)
                {
                    this.AdvanceOne();
                    return;
                }
            }
            else if (kind == TokenKind.RootGroup)
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
    /// Advances by the specified number of tokens, stopping at the end of the sequence.
    /// </summary>
    /// <param name="count">The number of tokens to skip.</param>
    public void Advance(int count)
        => this.MoveTo(Math.Min(this.Position + count, this.tokens.Length));

    /// <summary>
    /// Adds a diagnostic for the current token.
    /// </summary>
    /// <param name="code">The diagnostic.</param>
    /// <param name="obj">An optional diagnostic argument.</param>
    /// <param name="obj2">An optional diagnostic argument 2.</param>
    public void AddDiagnostic(DiagnosticCode code, object? obj = null, object? obj2 = null)
        => this.Diagnostic.Add(this.currentToken.Span, code, obj, obj2);

    /// <summary>
    /// Determines whether the specified token is an identifier with the given text.
    /// </summary>
    /// <param name="token">The token to examine.</param>
    /// <param name="identifier">The expected identifier text.</param>
    /// <returns><see langword="true"/> if the token matches the identifier; otherwise, <see langword="false"/>.</returns>
    public readonly bool IsIdentifierToken(Token token, ReadOnlySpan<char> identifier)
        => token.Kind == TokenKind.Identifier &&
            this.GetSpan(token).SequenceEqual(identifier);

    /// <summary>
    /// Determines whether the current token is an identifier with the given text.
    /// </summary>
    /// <param name="identifier">The expected identifier text.</param>
    /// <returns><see langword="true"/> if the current token matches the identifier; otherwise, <see langword="false"/>.</returns>
    public readonly bool IsCurrentIdentifier(ReadOnlySpan<char> identifier)
        => this.IsIdentifierToken(this.currentToken, identifier);

    /// <summary>
    /// Creates an error node at the current token.
    /// </summary>
    /// <returns>A new error node.</returns>
    public ErrorKoto NewErrorKoto()
        => new ErrorKoto(ref this, this.currentToken.Span) { Cause = this.Diagnostic.LastError };

    /// <summary>
    /// Gets the source text represented by the specified token.
    /// </summary>
    /// <param name="token">The token whose source text is requested.</param>
    /// <returns>The source span represented by the token.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ReadOnlySpan<char> GetSpan(Token token)
        => this.sourceText.Slice(token.Start, token.Length);

    /// <summary>
    /// Gets the interned identifier string represented by the specified token.
    /// </summary>
    /// <param name="token">The identifier-like token.</param>
    /// <returns>The shared string for the token text.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly string GetIdentifier(Token token)
    {
        var span = this.GetSpan(token);
        if (this.compilation.TryGetIdentifier(span, out var identifier))
        {
            return identifier;
        }

        this.Diagnostic.Add(token.Span, DiagnosticCode.InvalidIdentifier_Kd, span.ToString());
        return this.compilation.Intern(span); // Preserve the spelling for error recovery.
    }

    /// <summary>Validates and interns an identifier without allocating a syntax node.</summary>
    /// <param name="token">The identifier token.</param>
    /// <param name="identifier">The validated identifier text.</param>
    /// <returns>Whether the token contains a valid identifier.</returns>
    public readonly bool TryGetIdentifier(Token token, [NotNullWhen(true)] out string? identifier)
    {
        var span = this.GetSpan(token);
        if (token.Kind.IsIdentifierOrContextualKeyword() && this.compilation.TryGetIdentifier(span, out identifier))
        {
            return true;
        }

        return this.ReportInvalidIdentifier(token, out identifier);
    }

    /// <summary>
    /// Returns the textual representation of the current token.
    /// </summary>
    /// <returns>The textual representation of the current token.</returns>
    public readonly override string ToString()
        => this.GetSpan(this.currentToken).ToString();

    internal bool HasCompileTimeIfPrefix { get; set; }

    /// <summary>Gets or sets a value indicating whether primitive type names are accepted in a directive condition.</summary>
    internal bool IsParsingCompileTimeCondition { get; set; }

    internal readonly Token PeekToken(int offset)
    {
        var index = this.Position + offset;
        return (uint)index < (uint)this.tokens.Length ? this.tokens[index] : this.endToken;
    }

    // Split compound operators only in type context; shift/comparison expressions keep
    // their original tokens. The shared token buffer remains immutable.
    internal bool TryConsumeTypeClose(out SourceSpan range)
    {
        var remainingKind = this.currentToken.Kind switch
        {
            TokenKind.GreaterThanGreaterThan => TokenKind.GreaterThan,
            TokenKind.GreaterThanEquals => TokenKind.Equals,
            TokenKind.GreaterThanGreaterThanEquals => TokenKind.GreaterThanEquals,
            _ => TokenKind.Invalid,
        };
        if (remainingKind == TokenKind.Invalid)
        {
            range = this.currentToken.Span;
            return this.Expect(TokenKind.GreaterThan);
        }

        range = new SourceSpan(this.currentToken.Span.Start, 1);
        this.currentToken = new Token(remainingKind, SourceSpan.FromBounds(range.End, this.currentToken.Span.End));
        return true;
    }

    private bool TryConsumeWithRecovery(TokenKind targetKind, out SourceSpan range, bool addDiagnostic)
    {
Loop:
        if (this.CanRead)
        {
            var token = this.currentToken;
            if (token.Kind == targetKind)
            {
                range = token.Span;
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
                this.Expect(FormOf(targetKind));
                this.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
            }
        }

        // At the end of the sequence the tokenizer has already reported the missing closers.
        range = default;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AdvanceOne()
    {
        Debug.Assert(this.CanRead);
        this.MoveTo(this.Position + 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MoveTo(int position)
    {
        // A synthesized closer the reader passes is the tokenizer's missing form at its insertion point; what the parser expects
        // there next rests on it.
        for (var i = this.Position; i < position && i < this.tokens.Length; i++)
        {
            if (this.tokens[i].IsMissing)
            {
                this.NoteMissingCloser(i);
            }
        }

        this.Position = position;
        this.currentToken = (uint)position < (uint)this.tokens.Length ? this.tokens[position] : this.endToken;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private readonly bool ReportInvalidIdentifier(Token token, out string? identifier)
    {
        this.Diagnostic.Add(token.Span, DiagnosticCode.InvalidIdentifier_Kd, this.GetSpan(token).ToString());
        identifier = null;
        return false;
    }
}
