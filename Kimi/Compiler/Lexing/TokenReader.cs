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
public ref struct TokenReader
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
    private List<CompileTimeIfPrefix>? compileTimeIfPrefixes;

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

    /// <summary>
    /// Clears the current parser context.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearContext()
    {
        this.AttributeKoto = default;
        this.ModifierKind = default;
        this.IsExcluded = false;
        this.compileTimeIfPrefixes = default;
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
            this.AddDiagnostic(DiagnosticCode.IncompleteSyntax_Kd);
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
    /// <param name="code">An optional diagnostic hash reported for the first skipped token.</param>
    /// <returns>The token kind that stopped the scan, or the default value if the end was reached.</returns>
    public TokenKind SkipUntil(TokenKind kind1, DiagnosticCode code)
        => this.SkipUntil(kind1, kind1, kind1, code);

    /// <summary>
    /// Advances until either of the specified token kinds is reached.
    /// </summary>
    /// <param name="kind1">The first token kind at which to stop.</param>
    /// <param name="kind2">The second token kind at which to stop.</param>
    /// <param name="code">An optional diagnostic hash reported for the first skipped token.</param>
    /// <returns>The token kind that stopped the scan, or the default value if the end was reached.</returns>
    public TokenKind SkipUntil(TokenKind kind1, TokenKind kind2, DiagnosticCode code = DiagnosticCode.Template_Kd)
        => this.SkipUntil(kind1, kind2, kind2, code);

    /// <summary>
    /// Advances until any of the specified token kinds is reached.
    /// </summary>
    /// <param name="kind1">The first token kind at which to stop.</param>
    /// <param name="kind2">The second token kind at which to stop.</param>
    /// <param name="kind3">The third token kind at which to stop.</param>
    /// <param name="code">An optional diagnostic hash reported for the first skipped token.</param>
    /// <returns>The token kind that stopped the scan, or the default value if the end was reached.</returns>
    public TokenKind SkipUntil(
        TokenKind kind1,
        TokenKind kind2,
        TokenKind kind3,
        DiagnosticCode code = DiagnosticCode.Template_Kd)
    {
        while (this.CanRead)
        {
            var tokenKind = this.currentToken.Kind;
            if (tokenKind == kind1 || tokenKind == kind2 || tokenKind == kind3)
            {
                return tokenKind;
            }

            if (code != 0)
            {
                this.AddDiagnostic(code, this.GetSpan(this.currentToken).ToString());
                code = 0;
            }

            this.AdvanceOne();
        }

        return default;
    }

    /// <summary>
    /// Advances to the start of the immediately following block and reports at most one diagnostic
    /// for trailing tokens on the declaration line. Stops before a subsequent statement.
    /// </summary>
    /// <param name="code">The diagnostic reported for the first trailing token.</param>
    /// <returns>
    /// <see cref="TokenKind.StartBlock"/> when a block was found;
    /// otherwise, the default value.
    /// </returns>
    public TokenKind SkipUntilStartBlock(DiagnosticCode code = DiagnosticCode.UnexpectedTrailingToken_Kd)
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

            if (code != 0)
            {
                this.AddDiagnostic(code, this.GetSpan(this.currentToken).ToString());
                code = 0;
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
            this.SkipUntil(TokenKind.RootGroup, 0);
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
    /// Reports an unexpected-token diagnostic.
    /// </summary>
    /// <param name="token">The unexpected token.</param>
    public void ReportUnexpectedToken(Token token)
        => this.Diagnostic.Add(token.Span, DiagnosticCode.UnmatchedToken_Kd, token.Kind.ToText());

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
        => new ErrorKoto(ref this, this.currentToken.Span);

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
        => this.compilation.Intern(this.GetSpan(token));

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

        this.Diagnostic.Add(token.Span, DiagnosticCode.InvalidIdentifier_Kd, span.ToString());
        identifier = null;
        return false;
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
            return this.TryConsume(TokenKind.GreaterThan, out range, true);
        }

        range = new SourceSpan(this.currentToken.Span.Start, 1);
        this.currentToken = new Token(remainingKind, SourceSpan.FromBounds(range.End, this.currentToken.Span.End));
        return true;
    }

    /// <summary>Adds a deferred compile-time directive to the current syntax prefix.</summary>
    /// <param name="prefix">The directive and its parsed condition.</param>
    internal void AddCompileTimeIfPrefix(CompileTimeIfPrefix prefix)
        => (this.compileTimeIfPrefixes ??= []).Add(prefix);

    /// <summary>Detaches the deferred compile-time directives for the current syntax node.</summary>
    /// <returns>The detached directives, or <see langword="null"/> when none were deferred.</returns>
    internal List<CompileTimeIfPrefix>? TakeCompileTimeIfPrefixes()
    {
        var prefixes = this.compileTimeIfPrefixes;
        this.compileTimeIfPrefixes = default;
        return prefixes;
    }

    /// <summary>Discards deferred directives when an outer condition excludes the syntax.</summary>
    internal void ClearCompileTimeIfPrefixes()
        => this.compileTimeIfPrefixes = default;

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
                this.Diagnostic.Add(token.Span, DiagnosticCode.TokenMismatch_Kd, targetKind.ToText());
                this.SkipUntil(TokenKind.Separator, TokenKind.EndBlock, 0);
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
        this.Position = position;
        this.currentToken = (uint)position < (uint)this.tokens.Length ? this.tokens[position] : this.endToken;
    }
}
