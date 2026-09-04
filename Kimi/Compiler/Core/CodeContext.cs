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

    /// <summary>
    /// Gets the current compilation.
    /// </summary>
    public Compilation Compilation => this.Kotonoha.Compilation;

    /// <summary>
    /// Gets the root of the current Koto tree.
    /// </summary>
    public GroupKoto RootKoto => this.Kotonoha.RootKoto;

    internal CodeContext(Kotonoha kotonoha, DiagnosticCollection? customDiagnosticCollection = default)
    {
        ArgumentNullException.ThrowIfNull(kotonoha);

        this.Kotonoha = kotonoha;
        this.DiagnosticCollection = customDiagnosticCollection ?? kotonoha.DiagnosticCollection;
    }

    /// <summary>
    /// Parses source text and appends its nodes to a parent Declaration Container.
    /// </summary>
    /// <param name="parentKoto">The Declaration Container that receives the parsed nodes.</param>
    /// <param name="sourceText">The source text to parse.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parentKoto"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="parentKoto"/> belongs to another Kotonoha.</exception>
    public void Parse(DeclarationContainerKoto parentKoto, ReadOnlySpan<char> sourceText)
        => this.Parse(parentKoto, new SourceDocument(this.DiagnosticCollection.Name, sourceText.ToString()));

    /// <summary>
    /// Parses source text and appends its nodes to a parent Declaration Container.
    /// </summary>
    /// <param name="parentKoto">The Declaration Container that receives the parsed nodes.</param>
    /// <param name="sourceText">The source text to parse.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="parentKoto"/> belongs to another Kotonoha.</exception>
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
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="parentKoto"/> belongs to another Kotonoha.</exception>
    public void Parse(DeclarationContainerKoto parentKoto, SourceDocument sourceDocument)
    {
        ArgumentNullException.ThrowIfNull(parentKoto);
        ArgumentNullException.ThrowIfNull(sourceDocument);

        if (!ReferenceEquals(parentKoto.Kotonoha, this.Kotonoha))
        {
            throw new ArgumentException(
                "The destination Declaration Container must belong to the CodeContext's Kotonoha.",
                nameof(parentKoto));
        }

        var tokenizer = new Tokenizer(this.DiagnosticCollection, sourceDocument);
        try
        {
            tokenizer.ReadAll();
            var reader = new TokenReader(this, ref tokenizer);
            parentKoto.Parse(ref reader);
        }
        finally
        {
            tokenizer.Dispose();
        }
    }
}
