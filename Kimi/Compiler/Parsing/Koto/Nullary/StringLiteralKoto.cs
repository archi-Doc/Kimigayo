// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Lexing;

namespace Kimi.Compiler.Parsing;

[TinyhandObject]
public sealed partial class StringLiteralKoto : Koto
{
    [Key(1)]
    public string Literal { get; private set; }

    public StringLiteralKoto(ref TokenReader reader, Token token)
        : base(ref reader, token.Range)
    {
        this.Literal = token.Text.ToString();
    }

    public override string ToString()
        => $"{this.Literal}";

    public override void WriteTo(ref IndentedStringBuilder writer)
    {
        // writer.Append('\"');
        writer.Append(this.Literal);
        // writer.Append('\"');
    }

    public override (string Text, Koto[]? Children) Dump()
    {
        return ($"{this.GetType().Name}({this.Literal})", default);
    }
}
