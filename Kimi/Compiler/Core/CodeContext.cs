// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public class CodeContext
{
    public Compilation Compilation { get; }

    public Kotonoha Kotonoha { get; }

    public GroupKoto Root => this.Kotonoha.Root;

    public GroupKoto CurrentGroup { get; set; }

    internal CodeContext(Compilation compilation, Kotonoha kotonoha)
    {
        this.Compilation = compilation;
        this.Kotonoha = kotonoha;
        this.CurrentGroup = this.Kotonoha.Root;
    }
}
