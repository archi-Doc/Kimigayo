// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public class CodeContext
{
    public DiagnosticCollection DiagnosticCollection => this.Kotonoha.DiagnosticCollection;

    public Compilation Compilation => this.Kotonoha.Compilation;

    public Kotonoha Kotonoha { get; }

    public GroupKoto Root => this.Kotonoha.Root;

    // public GroupKoto CurrentGroup { get; set; }

    internal CodeContext(Kotonoha kotonoha)
    {
        this.Kotonoha = kotonoha;
        // this.CurrentGroup = this.Kotonoha.Root;
    }
}
