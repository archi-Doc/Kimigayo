// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public sealed partial class ExtensionKoto : GroupKoto
{
    public override KotoKind Akind => KotoKind.Extension;

    public ExtensionKoto(ref TokenReader reader, SourceRange range)
        : base(ref reader, range)
    {
    }

    internal ExtensionKoto(CodeContext codeContext, TokenContext state, SourceRange range)
        : base(codeContext, state, range)
    {
    }
}
