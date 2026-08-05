// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public class CodeContext
{
    public DiagnosticCollection Diagnostics { get; }

    public Compilation Compilation { get; }

    public Kotonoha Kotonoha { get; }

    // public ReadOnlyMemory<char> SourceText { get; }

    public GroupKoto Root => this.Kotonoha.Root;

    // public GroupKoto CurrentGroup { get; set; }

    internal CodeContext(DiagnosticCollection diagnosticCollection, Compilation compilation, Kotonoha kotonoha)
    {
        this.Diagnostics = diagnosticCollection;
        this.Compilation = compilation;
        this.Kotonoha = kotonoha;
        // this.CurrentGroup = this.Kotonoha.Root;
    }
}
