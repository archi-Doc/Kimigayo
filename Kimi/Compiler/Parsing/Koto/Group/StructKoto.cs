// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public sealed partial class StructKoto : GroupKoto
{
    public override KotoKind Akind => KotoKind.Struct;

    public StructKoto(ref TokenReader reader, SourceSpan range)
        : base(ref reader, range)
    {
    }

    internal StructKoto(CodeContext codeContext, TokenContext state, SourceSpan range)
        : base(codeContext, state, range)
    {
    }
}
