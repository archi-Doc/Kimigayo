// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Language;

public class CodeContext
{
    public Compilation Compilation { get; }

    public Kotonoha Kotonoha { get; }

    public GroupKoto CurrentGroup { get; set; }

    internal CodeContext(Compilation compilation, Kotonoha kotonoha)
    {
        this.Compilation = compilation;
        this.Kotonoha = kotonoha;
        this.CurrentGroup = this.Kotonoha.RootKoto;
    }
}
