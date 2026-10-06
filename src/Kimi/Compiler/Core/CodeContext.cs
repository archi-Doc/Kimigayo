// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

/// <summary>
/// Carries the compilation, source-unit, and diagnostic state used while tokenizing,
/// parsing, and generating Koto nodes.
/// </summary>
/// <remarks>
/// A context belongs to exactly one <see cref="Kotonoha"/>. Every node created through
/// the context is attached to that source unit, so parsing into a tree owned by another
/// source unit is rejected even when both source units belong to the same compilation.
/// </remarks>
public sealed class CodeContext
{
    // DIAGNOSTICS.md §4.3: synthesized syntax that parser recovery kept in place of a rejected form, with the key of the
    // syntax error it stands for. It lives with the nodes it maps and exists only after a recovery.
    private Dictionary<Koto, DiagnosticKey>? recoveries;

    /// <summary>
    /// Gets the diagnostic destination for this context.
    /// </summary>
    /// <remarks>
    /// Resolved once, because it is read for every node the parser creates.
    /// </remarks>
    public DiagnosticCollection DiagnosticCollection { get; }

    /// <summary>
    /// Gets the source unit being parsed.
    /// </summary>
    public Kotonoha Kotonoha { get; }

    /// <summary>Gets the immutable source snapshot, or null for a source-less parsing entry point.</summary>
    public SourceDocument? SourceDocument { get; }

    /// <summary>Gets optional documentation for this source snapshot.</summary>
    public Documentation.DocumentationSource? Documentation { get; internal set; }

    /// <summary>
    /// Gets the current compilation.
    /// </summary>
    public Compilation Compilation => this.Kotonoha.Compilation;

    /// <summary>
    /// Gets the root of the current Koto tree.
    /// </summary>
    public GroupKoto RootKoto => this.Kotonoha.RootKoto;

    internal CodeContext(Kotonoha kotonoha, DiagnosticCollection? customDiagnosticCollection = default, SourceDocument? sourceDocument = null)
    {
        ArgumentNullException.ThrowIfNull(kotonoha);

        this.Kotonoha = kotonoha;
        var diagnostics = customDiagnosticCollection ?? kotonoha.DiagnosticCollection;
        this.DiagnosticCollection = sourceDocument is null ? diagnostics : diagnostics.For(sourceDocument);
        this.SourceDocument = sourceDocument;
    }

    /// <summary>
    /// Parses source text and appends its nodes to a parent Declaration Container.
    /// </summary>
    /// <param name="parentKoto">The Declaration Container that receives the parsed nodes.</param>
    /// <param name="sourceText">The source text to parse.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parentKoto"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="parentKoto"/> belongs to another Kotonoha.</exception>
    /// <exception cref="InvalidOperationException">This context already belongs to a source snapshot.</exception>
    public void Parse(DeclarationContainerKoto parentKoto, ReadOnlySpan<char> sourceText)
        => this.Parse(parentKoto, new SourceDocument(this.DiagnosticCollection.Name, sourceText.ToString()));

    /// <summary>
    /// Parses source text and appends its nodes to a parent Declaration Container.
    /// </summary>
    /// <param name="parentKoto">The Declaration Container that receives the parsed nodes.</param>
    /// <param name="sourceText">The source text to parse.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="parentKoto"/> belongs to another Kotonoha.</exception>
    /// <exception cref="InvalidOperationException">This context already belongs to a source snapshot.</exception>
    public void Parse(DeclarationContainerKoto parentKoto, string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        this.Parse(parentKoto, new SourceDocument(this.DiagnosticCollection.Name, sourceText));
    }

    /// <summary>
    /// Parses a source document and appends its nodes to a parent Declaration Container.
    /// </summary>
    /// <param name="parentKoto">The Declaration Container that receives the parsed nodes.</param>
    /// <param name="sourceDocument">The source document to parse.</param>
    /// <param name="producingModId">The producing Mod ID for generated source, or null for ordinary source.</param>
    /// <param name="additionOrder">The source addition order within the producing Mod.</param>
    /// <remarks>Documents parsed into the root are retained for Kotonoha serialization.</remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="parentKoto"/> belongs to another Kotonoha.</exception>
    /// <exception cref="InvalidOperationException">This context already belongs to a source snapshot.</exception>
    public void Parse(DeclarationContainerKoto parentKoto, SourceDocument sourceDocument, string? producingModId = null, int additionOrder = 0)
    {
        ArgumentNullException.ThrowIfNull(parentKoto);
        ArgumentNullException.ThrowIfNull(sourceDocument);
        ArgumentOutOfRangeException.ThrowIfNegative(additionOrder);
        if (producingModId is { Length: 0 })
        {
            throw new ArgumentException("A producing Mod ID cannot be empty.", nameof(producingModId));
        }

        if (this.SourceDocument is not null)
        {
            throw new InvalidOperationException("Use a source-less parsing entry point to create a fresh context for each parse.");
        }

        if (!ReferenceEquals(parentKoto.Kotonoha, this.Kotonoha))
        {
            throw new ArgumentException(
                "The destination Declaration Container must belong to the CodeContext's Kotonoha.",
                nameof(parentKoto));
        }

        if (ReferenceEquals(parentKoto, this.RootKoto))
        {
            this.Kotonoha.RecordSource(sourceDocument, producingModId, additionOrder);
        }

        this.Compilation.BeginSourceParsing();
        // One target bound to the document serves the lexer and the parser, so parser recovery can rest on a lexical Error.
        var diagnostics = this.DiagnosticCollection.For(sourceDocument);
        var tokenizer = new Tokenizer(diagnostics, sourceDocument) { CollectDocumentation = this.Compilation.CollectDocumentation, OptionalHoverOwner = this.Compilation.CollectHover ? this.Compilation : null };
        try
        {
            tokenizer.ReadAll();
            // Nodes retain this immutable snapshot context; the source-less entry point can be reused.
            var sourceContext = new CodeContext(this.Kotonoha, diagnostics, sourceDocument) { Documentation = tokenizer.Documentation };
            var reader = new TokenReader(sourceContext, ref tokenizer);
            parentKoto.Parse(ref reader);
            sourceContext.Documentation?.SetLocation(this.Kotonoha.SourceDirectory, producingModId, additionOrder);
            sourceContext.Documentation?.Finish(this.Compilation.Diagnostics.HasSyntaxErrors(sourceDocument));
            this.Kotonoha.RecordDocumentation(sourceContext.Documentation);
        }
        finally
        {
            tokenizer.Dispose();
        }
    }

    /// <summary>Gets the syntax error that a recovered node stands for; a check that depends on the node's guessed form rests on it.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The key of the syntax error, or <see langword="null"/> when the node is not a recovery.</returns>
    internal DiagnosticKey? RecoveryCause(Koto node)
        => this.recoveries is { } map && map.TryGetValue(node, out var cause) ? cause : null;

    /// <summary>Gets a value indicating whether the parser recorded a recovery in this source; valid source records none.</summary>
    internal bool HasRecoveries => this.recoveries is not null;

    /// <summary>Records excluded syntax, so its diagnostics name the excluding directive (SPEC 19.5, 23.3.6.2).</summary>
    /// <param name="range">The excluded syntax.</param>
    /// <param name="directive">The innermost excluding directive.</param>
    internal void RecordExcludedRange(SourceSpan range, SourceSpan directive)
        => this.DiagnosticCollection.RecordExcludedRange(range, directive);

    /// <summary>Records that synthesized syntax stands for a syntax error.</summary>
    /// <param name="node">The synthesized node.</param>
    /// <param name="cause">The key of the syntax error.</param>
    internal void RecordRecovery(Koto node, DiagnosticKey cause)
        => (this.recoveries ??= new(ReferenceEqualityComparer.Instance))[node] = cause;
}
