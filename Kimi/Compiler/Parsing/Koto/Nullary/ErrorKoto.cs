// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;
using Kimi.Diagnostics;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public partial class ErrorKoto : Koto
{
    public ErrorKoto(ref TokenReader reader, SourceRange range)
        : base(ref reader, range)
    {
    }

    public override string ToString()
        => $"Error";

    public override void WriteTo(IndentWriter writer)
    {
        writer.Write("Error");
    }

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}", default);
    }
}
