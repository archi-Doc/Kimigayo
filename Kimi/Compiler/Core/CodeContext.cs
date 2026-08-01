// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public class CodeContext
{
    public Compilation Compilation { get; }

    public Kotonoha Kotonoha { get; }

    public ReadOnlyMemory<char> SourceText { get; }

    public GroupKoto Root => this.Kotonoha.Root;

    public GroupKoto CurrentGroup { get; set; }

    internal CodeContext(Compilation compilation, Kotonoha kotonoha, ReadOnlyMemory<char> sourceText)
    {
        this.Compilation = compilation;
        this.Kotonoha = kotonoha;
        this.SourceText = sourceText;
        this.CurrentGroup = this.Kotonoha.Root;
    }

    public ReadOnlySpan<char> GetSpan(Token token)
    {
        var start = 0;
        var length = 0;
        return this.SourceText.Span.Slice(start, length);
    }
}
