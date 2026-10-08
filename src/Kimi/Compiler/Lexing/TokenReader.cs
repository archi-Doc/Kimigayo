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
/// <param name="DispatchModifierSpan">The first virtual or override modifier, when present.</param>
public readonly record struct TokenContext(AttributeKoto? AttributeKoto, ModifierKind ModifierKind, bool IsExcluded, SourceSpan DispatchModifierSpan = default);

/// <summary>
/// The restrictions of the region the parser is in (SPEC 2.2.1): a new delimiter region lifts them, a body or header adds one, and
/// each enter method of <see cref="TokenReader"/> returns the region to restore afterwards.
/// </summary>
/// <param name="SingleBody">Whether the position lies in an expression body after <c>=&gt;</c>.</param>
/// <param name="IfBody">Whether the position lies in the body of an if.</param>
/// <param name="Header">Whether the position lies in a statement header.</param>
internal readonly record struct ParseRegion(bool SingleBody, bool IfBody, bool Header);

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

    // Region-local parsing restrictions; grouping and arm/item boundaries reset these.
    private ParseRegion region;

    internal SourceSpan DispatchModifierSpan { get; set; }

    /// <summary>Gets a value indicating whether the position lies in an expression body after <c>=&gt;</c>, where a nested arrow body is misplaced.</summary>
    internal readonly bool SingleBodyRegion => this.region.SingleBody;

    /// <summary>Gets a value indicating whether the position lies in the body of an if, where a nested if expression is misplaced.</summary>
    internal readonly bool IfBodyRegion => this.region.IfBody;

    /// <summary>Gets a value indicating whether the position lies in a statement header, where a body-bearing expression is misplaced.</summary>
    internal readonly bool HeaderRegion => this.region.Header;

    internal bool ConstraintRequirement { get; set; }

    /// <summary>
    /// Enters a new delimiter region (SPEC 2.2.1), which lifts every restriction of the enclosing one: a grouped expression, each
    /// argument or element in parentheses or brackets, an indented body, whose items are regions of their own, and a match arm.
    /// </summary>
    /// <returns>The region to restore afterwards.</returns>
    internal ParseRegion EnterRegion()
    {
        var previous = this.region;
        this.region = default;
        return previous;
    }

    /// <summary>Enters the expression body after <c>=&gt;</c>.</summary>
    /// <param name="ifBody">Whether the body belongs to an if.</param>
    /// <returns>The region to restore after the body.</returns>
    internal ParseRegion EnterSingleBody(bool ifBody)
    {
        var previous = this.region;
        this.region = new(true, previous.IfBody || ifBody, previous.Header);
        return previous;
    }

    /// <summary>Enters a statement header.</summary>
    /// <returns>The region to restore after the header.</returns>
    internal ParseRegion EnterHeader()
    {
        var previous = this.region;
        this.region = new(previous.SingleBody, previous.IfBody, true);
        return previous;
    }

    /// <summary>Restores the region that an enter method returned.</summary>
    /// <param name="region">The region to restore.</param>
    internal void RestoreRegion(ParseRegion region)
        => this.region = region;

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

    /// <summary>Gets the current token's span, or the insertion point after the last written token at a line boundary or the end.</summary>
    internal readonly SourceSpan InsertionSpan
        => this.currentToken.Kind is TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock || !this.CanRead
            ? new(this.PreviousSyntaxEnd, 0)
            : this.currentToken.Span;

    /// <summary>Gets the end of the last written token before the current position: where a missing form is inserted.</summary>
    internal readonly int PreviousSyntaxEnd
    {
        get
        {
            for (var i = this.Position - 1; i >= 0; i--)
            {
                if (!this.tokens[i].IsMissing && this.tokens[i].Kind is not (TokenKind.Separator or TokenKind.StartBlock or TokenKind.EndBlock))
                {
                    return this.tokens[i].Span.End;
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

            return last < 0 ? this.PreviousSyntaxEnd : this.tokens[last].Span.End;
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

    internal readonly void Hover(Koto syntax, SourceSpan token)
    {
        if (!this.InExcludedSyntax && !this.IsExcluded && this.CodeContext.SourceDocument is { } source)
        {
            this.compilation.RecordHover(syntax, source, token);
        }
    }

    internal readonly SourceSpan TokenSpanAt(int position) => this.tokens[position].Span;

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
        this.DispatchModifierSpan = default;
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
        var context = new TokenContext(this.AttributeKoto, this.ModifierKind, this.IsExcluded, this.DispatchModifierSpan);
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
        this.DispatchModifierSpan = context.DispatchModifierSpan;
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
    /// Consumes a token of the specified kind. An attribute at the position is misplaced (SPEC 6.5): it is reported and skipped,
    /// and the expected token may follow it.
    /// </summary>
    /// <param name="targetKind">The expected token kind.</param>
    /// <param name="range">The source range of the consumed token.</param>
    /// <param name="addDiagnostic">Whether to report the token's form as expected, and skip the rest of the line, when the token is not found.</param>
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
    /// Skips the indented block at the current <see cref="TokenKind.StartBlock"/> with the blocks nested in it; the tokenizer ends
    /// every block it starts, so the skip ends at the block's own <see cref="TokenKind.EndBlock"/>.
    /// </summary>
    public void SkipCurrentBlock()
    {
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
        => new ErrorKoto(ref this, this.InsertionSpan) { Cause = this.Diagnostic.LastError };

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

        this.ReportInvalidIdentifier(token);
        return this.compilation.Intern(span); // Preserve the spelling for error recovery.
    }

    /// <summary>Validates and interns an identifier without allocating a syntax node.</summary>
    /// <param name="token">The identifier token.</param>
    /// <param name="identifier">The validated identifier text.</param>
    /// <returns>Whether the token contains a valid identifier.</returns>
    public readonly bool TryGetIdentifier(Token token, [NotNullWhen(true)] out string? identifier)
    {
        if (token.Kind.IsIdentifierOrContextualKeyword() && this.compilation.TryGetIdentifier(this.GetSpan(token), out identifier))
        {
            return true;
        }

        this.ReportInvalidIdentifier(token);
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

    /// <summary>Gets the token at the specified offset from the current token without advancing; offset zero is the current token,
    /// which differs from the token at <see cref="Position"/> after <see cref="TryConsumeTypeClose"/> split it.</summary>
    /// <param name="offset">The number of tokens to look ahead.</param>
    /// <returns>The token, or the end token beyond the end of the sequence.</returns>
    internal readonly Token PeekToken(int offset)
    {
        if (offset == 0)
        {
            return this.currentToken;
        }

        var index = this.Position + offset;
        return (uint)index < (uint)this.tokens.Length ? this.tokens[index] : this.endToken;
    }

    /// <summary>
    /// Reports attributes at the current position, where the grammar takes none (SPEC 6.5): an attribute precedes a
    /// declaration, so one between the parts of a header, before a Type or in an expression is misplaced. Each is reported
    /// once over its whole extent and kept for the next node, so the source still round-trips through the tree.
    /// </summary>
    /// <param name="next">The token the grammar expects after the attributes, or <see cref="TokenKind.Invalid"/>. Where it is
    /// <c>(</c>, an attribute leaves the last parenthesized list to the grammar (<see cref="Parser.ParseAttributeKoto"/>).</param>
    /// <returns><see langword="true"/> when at least one attribute was read.</returns>
    internal bool ReportMisplacedAttributes(TokenKind next = TokenKind.Invalid)
    {
        var found = false;
        while (this.currentToken.Kind == TokenKind.Sharp)
        {
            if (Parser.ParseAttributeKoto(ref this, next == TokenKind.OpenParenthesis) is { } attribute)
            {
                // The kept attribute is this Error's recovery: Binding lets it mark nothing and checks the node it lands on alone.
                this.CodeContext.RecordRecovery(attribute, this.Unexpected(SyntaxForm.Attribute, attribute.Span));
            }

            found = true;
        }

        return found;
    }

    /// <summary>
    /// Consumes the '&gt;' that closes Type arguments or parameters, splitting '&gt;&gt;' and '&gt;&gt;=' only here, in Type
    /// context (SPEC 2.4); shift and comparison expressions keep their tokens, and the shared token buffer stays immutable.
    /// </summary>
    /// <param name="range">The consumed '&gt;'.</param>
    /// <param name="report">Whether a missing closer is reported; a list whose last argument failed rests on that Error instead.</param>
    /// <returns><see langword="true"/> when a closer was consumed.</returns>
    internal bool TryConsumeTypeClose(out SourceSpan range, bool report = true)
    {
        var remainingKind = this.currentToken.Kind switch
        {
            TokenKind.GreaterThanGreaterThan => TokenKind.GreaterThan,
            // Only the remainder of an original '>>=' can supply the second closer. A standalone '>=' is indivisible.
            TokenKind.GreaterThanEquals when this.tokens[this.Position].Kind == TokenKind.GreaterThanGreaterThanEquals => TokenKind.Equals,
            TokenKind.GreaterThanGreaterThanEquals => TokenKind.GreaterThanEquals,
            _ => TokenKind.Invalid,
        };
        if (remainingKind == TokenKind.Invalid)
        {
            range = this.currentToken.Span;
            return report ? this.Expect(TokenKind.GreaterThan) : this.TryConsume(TokenKind.GreaterThan);
        }

        range = new SourceSpan(this.currentToken.Span.Start, 1);
        this.currentToken = new Token(remainingKind, SourceSpan.FromBounds(range.End, this.currentToken.Span.End));
        return true;
    }

    /// <summary>
    /// Reports a problem with a code of its own at a span, unless an Error is already recorded there: a token is blamed once, so a
    /// token the lexer rejected rests on that Error (DIAGNOSTICS.md §4.4).
    /// </summary>
    /// <param name="span">The span of the problem.</param>
    /// <param name="code">The code.</param>
    /// <param name="argument">The optional message argument.</param>
    /// <returns>The key of the Error that explains the recovery, or <see langword="null"/> when the code is no Error.</returns>
    internal DiagnosticKey? ReportOnce(SourceSpan span, DiagnosticCode code, object? argument = null)
        => this.Diagnostic.RecallError(span) || this.Diagnostic.Add(span, code, argument) ? this.Diagnostic.LastError : null;

    /// <summary>
    /// Reads a Name (SPEC 2.5): a token that can be one is consumed and validated; any other token is where the Name was expected
    /// (DIAGNOSTICS.md §4.4) and is not consumed, so a line boundary, a closer or a comma still ends the construct. A token that
    /// the lexer rejected directly before a Name, such as a closer that closes nothing, is passed over: it was reported once.
    /// </summary>
    /// <param name="name">The validated Name, or <see langword="null"/> after the Error.</param>
    /// <param name="span">The token read, or the token where the Name was expected.</param>
    /// <returns>Whether a valid Name was read.</returns>
    internal bool TryReadName([NotNullWhen(true)] out string? name, out SourceSpan span)
    {
        if ((this.currentToken.Kind == TokenKind.Invalid || this.currentToken.ClosesNothing) && this.PeekKind(1).IsIdentifierOrContextualKeyword())
        {
            this.AdvanceOne();
        }

        var token = this.currentToken;
        span = token.Span;
        if (!token.Kind.IsIdentifierOrContextualKeyword())
        {
            this.Expect(SyntaxForm.Name);
            name = null;
            return false;
        }

        this.AdvanceOne();
        return this.TryGetIdentifier(token, out name);
    }

    /// <summary>
    /// Consumes the closer of a grouping. Any other token there is where the closer was expected (DIAGNOSTICS.md §4.4), and the rest
    /// of the grouping is then skipped to its closer, which is consumed. Nested groupings and bodies are skipped whole; the tokenizer
    /// closes every grouping before what follows it (SPEC 2.2.1), so the skip never leaves the grouping that failed: it stops at the
    /// closer of an enclosing grouping, the end of an enclosing body or a line boundary outside a nested body.
    /// </summary>
    /// <param name="closer">The closer: <c>)</c>, <c>]</c> or <c>}</c>.</param>
    /// <param name="range">The closer consumed, or the default value when none was found.</param>
    /// <returns><see langword="null"/> when the closer followed directly; otherwise the key of the Error, on which a node that the
    /// recovery completed rests.</returns>
    internal DiagnosticKey? ExpectCloser(TokenKind closer, out SourceSpan range)
    {
        if (!this.currentToken.ClosesNothing && this.TryConsume(closer, out range, false))
        {
            return null;
        }

        range = default;
        var cause = this.Expect(FormOf(closer));
        while (this.CanRead && !this.EndsGroupingContent())
        {
            this.SkipOne();
        }

        if (this.currentToken.Kind == closer && !this.currentToken.ClosesNothing)
        {
            range = this.currentToken.Span;
            this.AdvanceOne();
        }

        return cause;
    }

    /// <summary>
    /// Skips, without reporting, the rest of one item of a delimited list after the caller blamed it: up to the comma that ends the
    /// item or the end of the grouping's content (<see cref="ExpectCloser"/>). Groupings, bodies and recognized Type argument lists
    /// inside the item are skipped whole, so their commas and closers stay theirs.
    /// </summary>
    /// <param name="typeArguments">Whether the list is a Type argument or parameter list, which a '&gt;' at its level closes.</param>
    internal void SkipListItem(bool typeArguments = false)
    {
        var angles = 0;
        while (this.CanRead && !this.EndsGroupingContent())
        {
            switch (this.currentToken.Kind)
            {
                case TokenKind.Comma or TokenKind.Exclamation when angles == 0:
                    return; // A comma ends the item; so does a parameter list's '!' boundary, which is no operator elsewhere.

                case TokenKind.LessThan when this.currentToken.OpensTypeArguments:
                    angles++;
                    break;

                case TokenKind.GreaterThan or TokenKind.GreaterThanGreaterThan or TokenKind.GreaterThanEquals or TokenKind.GreaterThanGreaterThanEquals:
                    if (angles == 0)
                    {
                        if (typeArguments)
                        {
                            return;
                        }
                    }
                    else
                    {
                        angles = Math.Max(0, angles - (this.currentToken.Kind is TokenKind.GreaterThanGreaterThan or TokenKind.GreaterThanGreaterThanEquals ? 2 : 1));
                    }

                    break;
            }

            this.SkipOne();
        }
    }

    private bool TryConsumeWithRecovery(TokenKind targetKind, out SourceSpan range, bool addDiagnostic)
    {
        // An attribute where the grammar takes none is misplaced: it is reported and skipped, and the expected token may follow it.
        if (this.currentToken.Kind == TokenKind.Sharp && this.ReportMisplacedAttributes(targetKind) && this.currentToken.Kind == targetKind && this.CanRead)
        {
            range = this.currentToken.Span;
            this.AdvanceOne();
            return true;
        }

        if (addDiagnostic && this.CanRead)
        {
            this.Expect(FormOf(targetKind));
            this.SkipUntil(TokenKind.Separator, TokenKind.EndBlock);
        }

        // At the end of the sequence the tokenizer has already reported the missing closers.
        range = default;
        return false;
    }

    // Whether the current token ends the content of the grouping that a recovery skips in: a closer, the end of a body that encloses
    // the grouping, or a line boundary that opens no nested body. The tokenizer closes a grouping before any of the last two.
    private readonly bool EndsGroupingContent()
        => this.currentToken.Kind switch
        {
            TokenKind.CloseParenthesis or TokenKind.CloseBracket or TokenKind.CloseBrace => !this.currentToken.ClosesNothing,
            TokenKind.EndBlock => true,
            TokenKind.Separator => this.PeekKind(1) != TokenKind.StartBlock,
            _ => false,
        };

    // Skips the current token, or the nested grouping or body it opens whole, with the line boundaries around a nested body.
    private void SkipOne()
    {
        if (this.currentToken.Kind == TokenKind.Separator)
        {
            this.AdvanceOne(); // Inside a grouping, a line boundary opens a nested body (EndsGroupingContent).
        }

        var kind = this.currentToken.Kind;
        if (kind is not (TokenKind.OpenParenthesis or TokenKind.OpenBracket or TokenKind.OpenBrace or TokenKind.StartBlock))
        {
            this.AdvanceOne();
            return;
        }

        var nesting = 0;
        do
        {
            switch (this.currentToken.Kind)
            {
                case TokenKind.OpenParenthesis or TokenKind.OpenBracket or TokenKind.OpenBrace or TokenKind.StartBlock:
                    nesting++;
                    break;

                case TokenKind.CloseParenthesis or TokenKind.CloseBracket or TokenKind.CloseBrace when !this.currentToken.ClosesNothing:
                case TokenKind.EndBlock:
                    nesting--;
                    break;
            }

            this.AdvanceOne();
        }
        while (nesting > 0 && this.CanRead);

        if (kind == TokenKind.StartBlock && this.currentToken.Kind == TokenKind.Separator)
        {
            this.AdvanceOne(); // The dedent after a nested body separates nothing inside the grouping.
        }
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

    // A token is blamed once: one that the lexer or an earlier expectation rejected keeps that Error (DIAGNOSTICS.md §4.4).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private readonly void ReportInvalidIdentifier(Token token)
    {
        if (!this.Diagnostic.RecallError(token.Span))
        {
            this.Diagnostic.Add(token.Span, DiagnosticCode.InvalidIdentifier_Kd, this.GetSpan(token).ToString());
        }
    }
}
