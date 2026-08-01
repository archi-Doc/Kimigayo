// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public sealed partial class ContractKoto : GroupKoto
{
    public ContractKoto(ref TokenReader reader, SourceRange range)
        : base(ref reader, range)
    {
    }

    internal ContractKoto(CodeContext codeContext, TokenState state, SourceRange range)
        : base(codeContext, state, range)
    {
    }
}
