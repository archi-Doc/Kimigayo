// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Diagnostics;
using Kimi.Language;

[TinyhandObject]
public sealed partial class ContractKoto : GroupKoto
{
    public ContractKoto(ref TokenReader reader, SourceRange range)
        : base(ref reader, range)
    {
    }

    internal ContractKoto(CodeContext codeContext, TokenState state)
        : base(codeContext, state)
    {
    }
}
