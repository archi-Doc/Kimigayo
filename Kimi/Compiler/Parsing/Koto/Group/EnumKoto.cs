// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public sealed partial class EnumKoto : GroupKoto
{
    public override KotoKind Akind => KotoKind.Enum;

    public EnumKoto(ref TokenReader reader, TextSpan range)
        : base(ref reader, range)
    {
    }

    internal EnumKoto(CodeContext codeContext, TokenContext state, TextSpan range)
        : base(codeContext, state, range)
    {
    }
}
