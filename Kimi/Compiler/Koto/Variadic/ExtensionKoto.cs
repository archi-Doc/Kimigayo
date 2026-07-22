// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

[TinyhandObject]
public sealed partial class ExtensionKoto : GroupKoto
{
    public ExtensionKoto(ref TokenReader reader, SourceRange range)
        : base(ref reader, range)
    {
    }

    internal ExtensionKoto(CodeContext codeContext, TokenState state)
        : base(codeContext, state)
    {
    }
}
