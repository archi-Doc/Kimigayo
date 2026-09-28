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
        this.DiagnosticCollection = customDiagnosticCollection ?? kotonoha.DiagnosticCollection;
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
        var errorVersion = this.DiagnosticCollection.ErrorVersion;
        var tokenizer = new Tokenizer(this.DiagnosticCollection, sourceDocument) { CollectDocumentation = this.Compilation.CollectDocumentation };
        try
        {
            tokenizer.ReadAll();
            // Nodes retain this immutable snapshot context; the source-less entry point can be reused.
            var sourceContext = new CodeContext(this.Kotonoha, this.DiagnosticCollection, sourceDocument) { Documentation = tokenizer.Documentation };
            var reader = new TokenReader(sourceContext, ref tokenizer);
            parentKoto.Parse(ref reader);
            sourceContext.Documentation?.SetLocation(this.Compilation.Project.Directory, producingModId, additionOrder);
            sourceContext.Documentation?.Finish(this.DiagnosticCollection.ErrorVersion != errorVersion);
            this.Kotonoha.RecordDocumentation(sourceContext.Documentation);
            this.Kotonoha.RecordSourceErrors(this.DiagnosticCollection, errorVersion);
        }
        finally
        {
            tokenizer.Dispose();
        }
    }
}
