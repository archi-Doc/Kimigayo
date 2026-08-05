// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Lexing;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public class CodeContext
{
    public DiagnosticCollection DiagnosticCollection => this.Kotonoha.DiagnosticCollection;

    public Compilation Compilation => this.Kotonoha.Compilation;

    public Kotonoha Kotonoha { get; }

    public GroupKoto RootKoto => this.Kotonoha.RootKoto;

    // public GroupKoto CurrentGroup { get; set; }

    internal CodeContext(Kotonoha kotonoha)
    {
        this.Kotonoha = kotonoha;
        // this.CurrentGroup = this.Kotonoha.Root;
    }

    public void Parse(GroupKoto parentKoto, string sourceText)
        => this.Parse(parentKoto, sourceText.AsMemory());

    public void Parse(GroupKoto parentKoto, ReadOnlyMemory<char> sourceText)
    {
        if (parentKoto.CodeContext.Compilation != this.Compilation)
        {// Unmatched compilation
            return;
        }

        var tokenizer = new Tokenizer(this.DiagnosticCollection);
        tokenizer.Initialize(sourceText, 0, 0);

        var tokenBuilder = new TokenSequenceBuilder();
        try
        {
            tokenizer.ReadAll(ref tokenBuilder);
            var tokenSequence = tokenBuilder.ToReadOnlySequence();
            var reader = new TokenReader(this.DiagnosticCollection, this, tokenSequence);
            parentKoto.Parse(ref reader);
        }
        finally
        {
            tokenBuilder.Dispose();
        }
    }
}
