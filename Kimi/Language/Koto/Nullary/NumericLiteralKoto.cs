// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

[TinyhandObject]
public sealed partial class NumericLiteralKoto : Koto
{
    [Key(1)]
    public string Literal { get; private set; }

    public NumericLiteralKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Range)
    {
        this.Literal = token.Text.ToString();
    }

    public override string ToString()
        => $"{this.Literal}";

    public override void WriteTo(StringWriter writer)
    {
        writer.Write(this.Literal);
    }

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}({this.Literal})", default);
    }
}
