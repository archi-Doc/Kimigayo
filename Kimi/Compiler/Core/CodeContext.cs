// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public class CodeContext
{
    public DiagnosticCollection DiagnosticCollection => this.diagnosticCollection ?? this.Kotonoha.DiagnosticCollection;

    public Compilation Compilation => this.Kotonoha.Compilation;

    public Kotonoha Kotonoha { get; }

    public GroupKoto RootKoto => this.Kotonoha.RootKoto;

    private readonly DiagnosticCollection? diagnosticCollection;

    internal CodeContext(Kotonoha kotonoha, DiagnosticCollection? customDiagnosticCollection = default)
    {
        this.Kotonoha = kotonoha;
        this.diagnosticCollection = customDiagnosticCollection;
    }

    public void Parse(GroupKoto parentKoto, ReadOnlySpan<char> sourceText)
        => this.Parse(parentKoto, new SourceDocument(this.DiagnosticCollection.Name, sourceText.ToString()));

    public void Parse(GroupKoto parentKoto, string sourceText)
        => this.Parse(parentKoto, new SourceDocument(this.DiagnosticCollection.Name, sourceText));

    public void Parse(GroupKoto parentKoto, SourceDocument sourceDocument)
    {
        if (parentKoto.CodeContext.Compilation != this.Compilation)
        {// Unmatched compilation
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
