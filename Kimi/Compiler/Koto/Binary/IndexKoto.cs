// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class IndexKoto : BinaryKoto
{
    public Koto Index => this.Right;

    public IndexKoto(ref TokenReader reader, SourceRange range, Koto left, Koto index)
        : base(ref reader, range, left, index)
    {
    }

    public override string ToString()
        => $"{this.Left.ToString()}[{this.Index.ToString()}]";

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}", [this.Left, this.Index,]);
    }
}
