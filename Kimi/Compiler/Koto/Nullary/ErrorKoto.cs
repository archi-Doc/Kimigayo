// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;

namespace Kimi.Compiler;

[TinyhandObject]
public partial class ErrorKoto : Koto
{
    public ErrorKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Range)
    {
    }

    public override string ToString()
        => $"Error";

    public override void WriteTo(StringWriter writer)
    {
        writer.Write("Error");
    }

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}", default);
    }
}
