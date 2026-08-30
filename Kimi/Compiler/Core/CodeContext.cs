// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

/// <summary>
/// Provides compilation and diagnostic state while parsing Koto nodes.
/// </summary>
public class CodeContext
{
    /// <summary>
    /// Gets the diagnostic destination for this context.
    /// </summary>
    public DiagnosticCollection DiagnosticCollection => this.diagnosticCollection ?? this.Kotonoha.DiagnosticCollection;

    /// <summary>
    /// Gets the current compilation.
    /// </summary>
    public Compilation Compilation => this.Kotonoha.Compilation;

    /// <summary>
    /// Gets the source unit being parsed.
    /// </summary>
    public Kotonoha Kotonoha { get; }

    /// <summary>
    /// Gets the root of the current Koto tree.
    /// </summary>
    public GroupKoto RootKoto => this.Kotonoha.RootKoto;

    private readonly DiagnosticCollection? diagnosticCollection;

    internal CodeContext(Kotonoha kotonoha, DiagnosticCollection? customDiagnosticCollection = default)
    {
        this.Kotonoha = kotonoha;
        this.diagnosticCollection = customDiagnosticCollection;
    }

    /// <summary>
    /// Parses source text and appends its nodes to a parent group.
    /// </summary>
    /// <param name="parentKoto">The collection that receives the parsed nodes.</param>
    /// <param name="sourceText">The source text to parse.</param>
    public void Parse(CollectionKoto parentKoto, ReadOnlySpan<char> sourceText)
        => this.Parse(parentKoto, new SourceDocument(this.DiagnosticCollection.Name, sourceText.ToString()));

    /// <summary>
    /// Parses source text and appends its nodes to a parent group.
    /// </summary>
    /// <param name="parentKoto">The collection that receives the parsed nodes.</param>
    /// <param name="sourceText">The source text to parse.</param>
    public void Parse(CollectionKoto parentKoto, string sourceText)
        => this.Parse(parentKoto, new SourceDocument(this.DiagnosticCollection.Name, sourceText));

    /// <summary>
    /// Parses a source document and appends its nodes to a parent group.
    /// </summary>
    /// <param name="parentKoto">The collection that receives the parsed nodes.</param>
    /// <param name="sourceDocument">The source document to parse.</param>
    public void Parse(CollectionKoto parentKoto, SourceDocument sourceDocument)
    {
        if (parentKoto.CodeContext.Compilation != this.Compilation)
        {
            // A syntax tree cannot contain nodes from another compilation.
            return;
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
